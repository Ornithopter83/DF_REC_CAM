begin;

alter table public.nas_locations
    add column gateway_base_url text;

update public.nas_locations
   set gateway_base_url = 'https://dfblackbox-nas.duckdns.org:8443/dfblackbox/'
 where gateway_base_url is null;

alter table public.nas_locations
    alter column gateway_base_url set not null,
    add constraint nas_locations_gateway_base_url_check check (
        gateway_base_url ~ '^https://[A-Za-z0-9.-]+(:[0-9]+)?/'
        and gateway_base_url !~ '[@?#]'
        and right(gateway_base_url, 1) = '/'
    );

alter table public.devices
    add column last_public_ip inet,
    add column heartbeat_interval_seconds integer not null default 3
        check (heartbeat_interval_seconds between 2 and 300);

drop function public.get_device_stream_command(uuid, text);

create function public.get_device_stream_command(
    p_device_id uuid,
    p_device_token text,
    p_public_ip text default null
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
declare
    v_public_ip inet;
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

    if nullif(trim(coalesce(p_public_ip, '')), '') is not null then
        begin
            v_public_ip := trim(p_public_ip)::inet;
        exception when invalid_text_representation then
            v_public_ip := null;
        end;
    end if;

    update public.devices
       set last_seen_at = now(),
           last_public_ip = coalesce(v_public_ip, last_public_ip)
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
    select stream.camera_id,
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

create function public.list_portal_cameras()
returns table (
    camera_id uuid,
    display_name text,
    camera_type text,
    connection_state text,
    storage_state text,
    device_id uuid,
    device_last_seen_at timestamptz,
    device_public_ip text,
    heartbeat_interval_seconds integer,
    device_online boolean
)
language sql
stable
security definer
set search_path = ''
as $$
    select camera.id,
           camera.display_name,
           camera.camera_type,
           camera.connection_state,
           camera.storage_state,
           device.id,
           device.last_seen_at,
           host(device.last_public_ip),
           device.heartbeat_interval_seconds,
           device.last_seen_at is not null
               and device.last_seen_at >= now() - make_interval(
                   secs => greatest(device.heartbeat_interval_seconds * 3, 15)
               )
      from public.cameras camera
      join public.devices device on device.id = camera.device_id
     where device.revoked_at is null
       and private.is_organization_member(device.organization_id)
     order by camera.created_at desc, camera.id desc;
$$;

create function public.get_user_nas_session_scopes()
returns table (
    user_id uuid,
    gateway_base_url text,
    scopes jsonb
)
language sql
stable
security definer
set search_path = ''
as $$
    with authorized_cameras as (
        select location.id as nas_location_id,
               location.gateway_base_url,
               camera.nas_relative_path
          from public.organization_members member
          join public.devices device
            on device.organization_id = member.organization_id
           and device.revoked_at is null
          join public.cameras camera
            on camera.device_id = device.id
           and camera.nas_location_id is not null
           and camera.nas_relative_path is not null
          join public.nas_locations location
            on location.id = camera.nas_location_id
           and location.is_active
         where member.user_id = auth.uid()
           and member.is_active
    ),
    location_scopes as (
        select authorized.nas_location_id,
               authorized.gateway_base_url,
               array_agg(distinct trim(both '/' from authorized.nas_relative_path)) as prefixes
          from authorized_cameras authorized
         group by authorized.nas_location_id, authorized.gateway_base_url
    )
    select auth.uid(),
           scope.gateway_base_url,
           jsonb_agg(
               jsonb_build_object(
                   'location_id', scope.nas_location_id,
                   'prefixes', to_jsonb(scope.prefixes)
               )
               order by scope.nas_location_id
           )
      from location_scopes scope
     where auth.uid() is not null
     group by scope.gateway_base_url;
$$;

drop function public.get_recording_nas_access(uuid);

create function public.get_recording_nas_access(p_recording_id uuid)
returns table (
    recording_id uuid,
    original_file_name text,
    nas_location_id uuid,
    gateway_base_url text,
    nas_relative_path text
)
language plpgsql
stable
security definer
set search_path = ''
as $$
begin
    return query
    select recording.id,
           recording.original_file_name,
           location.id,
           location.gateway_base_url,
           recording.nas_relative_path
      from public.recordings recording
      join public.nas_locations location
        on location.id = recording.nas_location_id
       and location.is_active
     where recording.id = p_recording_id
       and recording.sync_state = 'ready'
       and private.is_organization_member(recording.organization_id);
end;
$$;

revoke execute on function public.get_device_stream_command(uuid, text, text)
    from public, anon, authenticated;
grant execute on function public.get_device_stream_command(uuid, text, text)
    to service_role;

revoke execute on function public.list_portal_cameras() from public, anon;
grant execute on function public.list_portal_cameras() to authenticated, service_role;

revoke execute on function public.get_user_nas_session_scopes() from public, anon;
grant execute on function public.get_user_nas_session_scopes() to authenticated, service_role;

revoke execute on function public.get_recording_nas_access(uuid) from public, anon;
grant execute on function public.get_recording_nas_access(uuid)
    to authenticated, service_role;

notify pgrst, 'reload schema';

commit;
