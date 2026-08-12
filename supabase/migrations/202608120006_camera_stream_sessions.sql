begin;

create table public.camera_streams (
    camera_id uuid primary key references public.cameras(id) on delete cascade,
    device_id uuid not null references public.devices(id) on delete cascade,
    room_name text not null unique check (char_length(room_name) between 1 and 120),
    state text not null default 'idle'
        check (state in ('idle', 'requested', 'publishing', 'error')),
    viewer_lease_until timestamptz,
    ingress_id text unique,
    ingress_url text,
    ingress_stream_key text,
    ingress_creation_started_at timestamptz,
    last_device_poll_at timestamptz,
    last_started_at timestamptz,
    last_stopped_at timestamptz,
    error_code text check (error_code is null or char_length(error_code) between 1 and 80),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    check (
        (ingress_id is null and ingress_url is null and ingress_stream_key is null)
        or (ingress_id is not null and ingress_url is not null and ingress_stream_key is not null)
    )
);

create index camera_streams_device_id_idx on public.camera_streams (device_id);
create index camera_streams_viewer_lease_idx on public.camera_streams (viewer_lease_until);

create trigger camera_streams_set_updated_at
before update on public.camera_streams
for each row execute function private.set_updated_at();

alter table public.camera_streams enable row level security;
revoke all on table public.camera_streams from anon, authenticated;
grant all on table public.camera_streams to service_role;

create or replace function public.request_camera_stream(p_camera_id uuid)
returns table (
    camera_id uuid,
    device_id uuid,
    room_name text,
    viewer_identity text,
    lease_until timestamptz
)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_user_id uuid := (select auth.uid());
    v_device_id uuid;
    v_organization_id uuid;
    v_room_name text;
    v_lease_until timestamptz := now() + interval '90 seconds';
begin
    if v_user_id is null then
        raise exception 'authentication_required';
    end if;

    select device.id, device.organization_id
      into v_device_id, v_organization_id
      from public.cameras camera
      join public.devices device on device.id = camera.device_id
     where camera.id = p_camera_id
       and device.registration_state = 'active'
       and device.revoked_at is null;

    if v_device_id is null
       or not (select private.is_organization_member(v_organization_id)) then
        raise exception 'operation_not_authorized';
    end if;

    v_room_name := 'camera-' || replace(p_camera_id::text, '-', '');
    insert into public.camera_streams (
        camera_id,
        device_id,
        room_name,
        state,
        viewer_lease_until
    )
    values (
        p_camera_id,
        v_device_id,
        v_room_name,
        'requested',
        v_lease_until
    )
    on conflict (camera_id) do update
       set viewer_lease_until = greatest(
               coalesce(public.camera_streams.viewer_lease_until, '-infinity'::timestamptz),
               excluded.viewer_lease_until),
           state = case
               when public.camera_streams.state = 'publishing' then 'publishing'
               else 'requested'
           end,
           error_code = null;

    return query select
        p_camera_id,
        v_device_id,
        v_room_name,
        v_user_id::text,
        v_lease_until;
end;
$$;

create or replace function public.get_camera_stream_configuration(p_camera_id uuid)
returns table (
    camera_id uuid,
    device_id uuid,
    room_name text,
    ingress_id text,
    ingress_url text,
    ingress_stream_key text,
    ingress_creation_started_at timestamptz,
    lease_until timestamptz,
    state text
)
language sql
security definer
set search_path = ''
as $$
    select
        stream.camera_id,
        stream.device_id,
        stream.room_name,
        stream.ingress_id,
        stream.ingress_url,
        stream.ingress_stream_key,
        stream.ingress_creation_started_at,
        stream.viewer_lease_until,
        stream.state
    from public.camera_streams stream
    where stream.camera_id = p_camera_id;
$$;

create or replace function public.begin_camera_stream_ingress(p_camera_id uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    update public.camera_streams stream
       set ingress_creation_started_at = now(),
           error_code = null
     where stream.camera_id = p_camera_id
       and stream.ingress_id is null
       and (
           stream.ingress_creation_started_at is null
           or stream.ingress_creation_started_at < now() - interval '2 minutes'
       );
    return found;
end;
$$;

create or replace function public.complete_camera_stream_ingress(
    p_camera_id uuid,
    p_ingress_id text,
    p_ingress_url text,
    p_ingress_stream_key text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    if char_length(trim(p_ingress_id)) not between 1 and 256
       or char_length(trim(p_ingress_url)) not between 1 and 1024
       or char_length(trim(p_ingress_stream_key)) not between 1 and 512 then
        raise exception 'invalid_ingress_configuration';
    end if;

    update public.camera_streams stream
       set ingress_id = trim(p_ingress_id),
           ingress_url = trim(p_ingress_url),
           ingress_stream_key = trim(p_ingress_stream_key),
           ingress_creation_started_at = null,
           error_code = null
     where stream.camera_id = p_camera_id;
    return found;
end;
$$;

create or replace function public.fail_camera_stream_ingress(
    p_camera_id uuid,
    p_error_code text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    update public.camera_streams stream
       set ingress_creation_started_at = null,
           state = 'error',
           error_code = left(coalesce(nullif(trim(p_error_code), ''), 'ingress_creation_failed'), 80)
     where stream.camera_id = p_camera_id
       and stream.ingress_id is null;
    return found;
end;
$$;

create or replace function public.get_device_stream_command(
    p_device_id uuid,
    p_device_token text
)
returns table (
    camera_id uuid,
    room_name text,
    should_stream boolean,
    ingress_url text,
    ingress_stream_key text,
    lease_until timestamptz
)
language plpgsql
security definer
set search_path = ''
as $$
begin
    if not exists (
        select 1
        from public.devices device
        where device.id = p_device_id
          and device.registration_state = 'active'
          and device.revoked_at is null
          and device.token_hash = extensions.digest(p_device_token, 'sha256')
    ) then
        raise exception 'device_authentication_failed';
    end if;

    update public.devices
       set last_seen_at = now()
     where id = p_device_id;

    update public.camera_streams stream
       set last_device_poll_at = now(),
           state = case
               when coalesce(stream.viewer_lease_until, '-infinity'::timestamptz) <= now()
                    and stream.state in ('requested', 'publishing') then 'idle'
               else stream.state
           end,
           last_stopped_at = case
               when coalesce(stream.viewer_lease_until, '-infinity'::timestamptz) <= now()
                    and stream.state = 'publishing' then now()
               else stream.last_stopped_at
           end
     where stream.device_id = p_device_id;

    return query
    select
        stream.camera_id,
        stream.room_name,
        coalesce(stream.viewer_lease_until, '-infinity'::timestamptz) > now()
            and stream.ingress_id is not null,
        stream.ingress_url,
        stream.ingress_stream_key,
        stream.viewer_lease_until
    from public.camera_streams stream
    where stream.device_id = p_device_id
    order by stream.updated_at desc
    limit 1;
end;
$$;

create or replace function public.report_camera_stream_state(
    p_device_id uuid,
    p_device_token text,
    p_camera_id uuid,
    p_state text,
    p_error_code text default null
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    if p_state not in ('idle', 'publishing', 'error') then
        raise exception 'invalid_stream_state';
    end if;

    if not exists (
        select 1
        from public.devices device
        where device.id = p_device_id
          and device.registration_state = 'active'
          and device.revoked_at is null
          and device.token_hash = extensions.digest(p_device_token, 'sha256')
    ) then
        raise exception 'device_authentication_failed';
    end if;

    update public.camera_streams stream
       set state = p_state,
           error_code = case
               when p_state = 'error'
                   then left(coalesce(nullif(trim(p_error_code), ''), 'stream_publish_failed'), 80)
               else null
           end,
           last_started_at = case when p_state = 'publishing' then now() else stream.last_started_at end,
           last_stopped_at = case when p_state = 'idle' then now() else stream.last_stopped_at end
     where stream.camera_id = p_camera_id
       and stream.device_id = p_device_id;

    if not found then
        raise exception 'stream_not_found';
    end if;
    return true;
end;
$$;

revoke execute on function public.request_camera_stream(uuid) from public, anon;
grant execute on function public.request_camera_stream(uuid) to authenticated, service_role;

revoke execute on function public.get_camera_stream_configuration(uuid)
    from public, anon, authenticated;
revoke execute on function public.begin_camera_stream_ingress(uuid)
    from public, anon, authenticated;
revoke execute on function public.complete_camera_stream_ingress(uuid, text, text, text)
    from public, anon, authenticated;
revoke execute on function public.fail_camera_stream_ingress(uuid, text)
    from public, anon, authenticated;
revoke execute on function public.get_device_stream_command(uuid, text)
    from public, anon, authenticated;
revoke execute on function public.report_camera_stream_state(uuid, text, uuid, text, text)
    from public, anon, authenticated;

grant execute on function public.get_camera_stream_configuration(uuid) to service_role;
grant execute on function public.begin_camera_stream_ingress(uuid) to service_role;
grant execute on function public.complete_camera_stream_ingress(uuid, text, text, text) to service_role;
grant execute on function public.fail_camera_stream_ingress(uuid, text) to service_role;
grant execute on function public.get_device_stream_command(uuid, text) to service_role;
grant execute on function public.report_camera_stream_state(uuid, text, uuid, text, text) to service_role;

commit;
