begin;

create table public.nas_locations (
    id uuid primary key default gen_random_uuid(),
    organization_id uuid not null references public.organizations(id) on delete cascade,
    name text not null,
    download_base_url text not null,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (organization_id, name),
    check (char_length(name) between 1 and 80),
    check (download_base_url ~ '^https://[A-Za-z0-9.-]+(:[0-9]+)?/'),
    check (download_base_url !~ '[@?#]'),
    check (right(download_base_url, 1) = '/')
);

create trigger nas_locations_set_updated_at
before update on public.nas_locations
for each row execute function private.set_updated_at();

alter table public.nas_locations enable row level security;
revoke all on table public.nas_locations from public, anon, authenticated;
grant all on table public.nas_locations to service_role;

insert into public.nas_locations (organization_id, name, download_base_url)
select organization.id,
       'NAS1dual',
       'https://dfblackbox-nas.duckdns.org/list/HDD1/Media/'
  from public.organizations organization
on conflict (organization_id, name) do update
set download_base_url = excluded.download_base_url,
    is_active = true;

alter table public.cameras
    add column nas_location_id uuid references public.nas_locations(id) on delete restrict;

update public.cameras camera
   set nas_location_id = location.id
  from public.devices device
  join public.nas_locations location
    on location.organization_id = device.organization_id
   and location.name = 'NAS1dual'
   and location.is_active
 where device.id = camera.device_id
   and camera.nas_location_id is null;

create or replace function private.assign_default_camera_nas_location()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if new.nas_location_id is null then
        select location.id
          into new.nas_location_id
          from public.devices device
          join public.nas_locations location
            on location.organization_id = device.organization_id
           and location.is_active
         where device.id = new.device_id
         order by location.created_at, location.id
         limit 1;
    end if;
    return new;
end;
$$;

create trigger cameras_assign_default_nas_location
before insert or update of device_id, nas_location_id on public.cameras
for each row execute function private.assign_default_camera_nas_location();

alter table public.recordings
    alter column storage_bucket drop not null,
    alter column storage_bucket drop default,
    alter column storage_object_path drop not null,
    add column nas_location_id uuid references public.nas_locations(id) on delete restrict,
    add column nas_relative_path text,
    add column cataloged_at timestamptz;

do $$
declare
    constraint_name text;
begin
    select check_constraint.conname
      into constraint_name
      from pg_constraint check_constraint
     where check_constraint.conrelid = 'public.recordings'::regclass
       and check_constraint.contype = 'c'
       and pg_get_constraintdef(check_constraint.oid) like '%sync_state%ready%uploaded_at%'
     limit 1;
    if constraint_name is not null then
        execute format('alter table public.recordings drop constraint %I', constraint_name);
    end if;
end;
$$;

alter table public.recordings
    add constraint recordings_nas_relative_path_check check (
        nas_relative_path is null
        or (
            char_length(nas_relative_path) between 1 and 1024
            and nas_relative_path !~ '(^/|(^|/)\.{1,2}(/|$)|:)'
            and position(chr(92) in nas_relative_path) = 0
            and position('//' in nas_relative_path) = 0
            and lower(nas_relative_path) like '%.mp4'
        )
    ),
    add constraint recordings_catalog_location_check check (
        (nas_location_id is null and nas_relative_path is null and cataloged_at is null)
        or (nas_location_id is not null and nas_relative_path is not null and cataloged_at is not null)
    );

create index recordings_nas_location_path_idx
    on public.recordings (nas_location_id, nas_relative_path)
    where nas_location_id is not null;

create or replace function public.register_device_recording_catalog(
    p_device_id uuid,
    p_device_token text,
    p_camera_id uuid,
    p_nas_relative_path text,
    p_original_file_name text,
    p_file_size_bytes bigint,
    p_source_fingerprint text,
    p_recorded_at timestamptz,
    p_duration_seconds numeric default null,
    p_source_last_modified_at timestamptz default null
)
returns table (recording_id uuid, catalog_state text)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_organization_id uuid;
    v_camera_nas_path text;
    v_nas_location_id uuid;
    v_recording public.recordings%rowtype;
    v_path text := trim(p_nas_relative_path);
    v_file_name text := trim(p_original_file_name);
    v_fingerprint text := lower(trim(p_source_fingerprint));
begin
    select device.organization_id,
           camera.nas_relative_path,
           camera.nas_location_id
      into v_organization_id, v_camera_nas_path, v_nas_location_id
      from public.devices device
      join public.cameras camera
        on camera.device_id = device.id
       and camera.id = p_camera_id
     where device.id = p_device_id
       and device.registration_state = 'active'
       and device.revoked_at is null
       and device.token_hash = extensions.digest(p_device_token, 'sha256')
     for update of device, camera;

    if not found then
        if not exists (
            select 1 from public.devices device
             where device.id = p_device_id
               and device.registration_state = 'active'
               and device.revoked_at is null
               and device.token_hash = extensions.digest(p_device_token, 'sha256')
        ) then
            raise exception 'device_authentication_failed';
        end if;
        raise exception 'camera_not_found';
    end if;
    if v_nas_location_id is null then
        raise exception 'nas_location_not_configured';
    end if;
    if char_length(v_path) not between 1 and 1024
       or v_path ~ '(^/|(^|/)\.{1,2}(/|$)|:)'
       or position(chr(92) in v_path) > 0
       or position('//' in v_path) > 0
       or lower(v_path) not like '%.mp4'
       or v_path not like trim(both '/' from v_camera_nas_path) || '/recordings/%' then
        raise exception 'invalid_nas_relative_path';
    end if;
    if char_length(v_file_name) not between 1 and 255
       or position('/' in v_file_name) > 0
       or position(chr(92) in v_file_name) > 0
       or lower(v_file_name) not like '%.mp4'
       or right(v_path, char_length(v_file_name)) <> v_file_name then
        raise exception 'invalid_original_file_name';
    end if;
    if v_fingerprint !~ '^[0-9a-f]{64}$' then
        raise exception 'invalid_source_fingerprint';
    end if;
    if p_file_size_bytes not between 1 and 10995116277760 then
        raise exception 'invalid_file_size';
    end if;
    if p_recorded_at is null or p_recorded_at > now() + interval '5 minutes' then
        raise exception 'invalid_recorded_at';
    end if;
    if p_duration_seconds is not null and p_duration_seconds not between 0 and 604800 then
        raise exception 'invalid_duration_seconds';
    end if;
    if p_source_last_modified_at is not null
       and p_source_last_modified_at > now() + interval '5 minutes' then
        raise exception 'invalid_source_last_modified_at';
    end if;

    select recording.*
      into v_recording
      from public.recordings recording
     where recording.device_id = p_device_id
       and recording.camera_id = p_camera_id
       and recording.source_fingerprint = v_fingerprint
     for update;

    if not found then
        insert into public.recordings (
            organization_id, device_id, camera_id,
            source_relative_path, original_file_name, source_fingerprint,
            source_last_modified_at, recorded_at, duration_seconds,
            file_size_bytes, sync_state, nas_location_id,
            nas_relative_path, cataloged_at
        ) values (
            v_organization_id, p_device_id, p_camera_id,
            v_path, v_file_name, v_fingerprint,
            p_source_last_modified_at, p_recorded_at, p_duration_seconds,
            p_file_size_bytes, 'ready', v_nas_location_id,
            v_path, now()
        )
        on conflict (device_id, camera_id, source_fingerprint) do nothing;

        select recording.*
          into v_recording
          from public.recordings recording
         where recording.device_id = p_device_id
           and recording.camera_id = p_camera_id
           and recording.source_fingerprint = v_fingerprint
         for update;
    end if;

    if v_recording.file_size_bytes <> p_file_size_bytes then
        raise exception 'fingerprint_metadata_conflict';
    end if;

    update public.recordings recording
       set source_relative_path = v_path,
           original_file_name = v_file_name,
           source_last_modified_at = coalesce(p_source_last_modified_at, recording.source_last_modified_at),
           source_last_seen_at = now(),
           duration_seconds = coalesce(p_duration_seconds, recording.duration_seconds),
           nas_location_id = v_nas_location_id,
           nas_relative_path = v_path,
           cataloged_at = now(),
           sync_state = 'ready',
           error_code = null
     where recording.id = v_recording.id
     returning recording.* into v_recording;

    update public.devices set last_seen_at = now() where id = p_device_id;
    return query select v_recording.id, 'ready'::text;
end;
$$;

create or replace function public.list_camera_recordings(
    p_camera_id uuid,
    p_limit integer default 51,
    p_cursor_recorded_at timestamptz default null,
    p_cursor_id uuid default null
)
returns table (
    recording_id uuid,
    camera_id uuid,
    original_file_name text,
    recorded_at timestamptz,
    duration_seconds numeric,
    file_size_bytes bigint
)
language plpgsql
stable
security definer
set search_path = ''
as $$
declare
    v_organization_id uuid;
begin
    if (p_cursor_recorded_at is null) <> (p_cursor_id is null) then
        raise exception 'invalid_recording_cursor';
    end if;
    select device.organization_id into v_organization_id
      from public.cameras camera
      join public.devices device on device.id = camera.device_id
     where camera.id = p_camera_id;
    if not found then raise exception 'camera_not_found'; end if;
    if not private.is_organization_member(v_organization_id) then
        raise exception 'recording_access_denied';
    end if;
    return query
    select recording.id, recording.camera_id, recording.original_file_name,
           recording.recorded_at, recording.duration_seconds, recording.file_size_bytes
      from public.recordings recording
     where recording.camera_id = p_camera_id
       and recording.sync_state = 'ready'
       and recording.nas_location_id is not null
       and recording.nas_relative_path is not null
       and (p_cursor_recorded_at is null
            or (recording.recorded_at, recording.id) < (p_cursor_recorded_at, p_cursor_id))
     order by recording.recorded_at desc, recording.id desc
     limit least(greatest(coalesce(p_limit, 51), 1), 101);
end;
$$;

drop function if exists public.get_recording_media_access(uuid);

create function public.get_recording_nas_access(p_recording_id uuid)
returns table (
    recording_id uuid,
    original_file_name text,
    download_base_url text,
    nas_relative_path text
)
language plpgsql
stable
security definer
set search_path = ''
as $$
begin
    return query
    select recording.id, recording.original_file_name,
           location.download_base_url, recording.nas_relative_path
      from public.recordings recording
      join public.nas_locations location
        on location.id = recording.nas_location_id
       and location.is_active
     where recording.id = p_recording_id
       and recording.sync_state = 'ready'
       and private.is_organization_member(recording.organization_id);
end;
$$;

revoke execute on function public.begin_device_recording_upload(
    uuid, text, uuid, text, text, bigint, text, timestamptz, numeric, timestamptz
) from service_role;
revoke execute on function public.complete_device_recording_upload(
    uuid, text, uuid, bigint, text
) from service_role;
revoke execute on function public.get_device_recording_upload(
    uuid, text, uuid, bigint, text
) from service_role;

revoke execute on function public.register_device_recording_catalog(
    uuid, text, uuid, text, text, bigint, text, timestamptz, numeric, timestamptz
) from public, anon, authenticated;
grant execute on function public.register_device_recording_catalog(
    uuid, text, uuid, text, text, bigint, text, timestamptz, numeric, timestamptz
) to service_role;
revoke execute on function public.get_recording_nas_access(uuid) from public, anon;
grant execute on function public.get_recording_nas_access(uuid) to authenticated, service_role;

commit;
