begin;

insert into storage.buckets (
    id,
    name,
    public,
    file_size_limit,
    allowed_mime_types
)
values (
    'dfblackbox-recordings',
    'dfblackbox-recordings',
    false,
    null,
    array['video/mp4']::text[]
)
on conflict (id) do update
set public = false,
    allowed_mime_types = excluded.allowed_mime_types;

do $$
begin
    if not exists (
        select 1
          from pg_constraint
         where conrelid = 'public.devices'::regclass
           and conname = 'devices_id_organization_unique'
    ) then
        alter table public.devices
            add constraint devices_id_organization_unique unique (id, organization_id);
    end if;

    if not exists (
        select 1
          from pg_constraint
         where conrelid = 'public.cameras'::regclass
           and conname = 'cameras_id_device_unique'
    ) then
        alter table public.cameras
            add constraint cameras_id_device_unique unique (id, device_id);
    end if;
end;
$$;

create table public.recordings (
    id uuid primary key default gen_random_uuid(),
    organization_id uuid not null references public.organizations(id) on delete restrict,
    device_id uuid not null,
    camera_id uuid not null,
    source_relative_path text not null,
    original_file_name text not null,
    source_fingerprint text not null,
    source_last_modified_at timestamptz,
    source_last_seen_at timestamptz not null default now(),
    recorded_at timestamptz not null,
    duration_seconds numeric(12, 3),
    file_size_bytes bigint not null,
    storage_bucket text not null default 'dfblackbox-recordings',
    storage_object_path text not null unique,
    sync_state text not null default 'uploading'
        check (sync_state in ('uploading', 'ready', 'upload_error')),
    error_code text check (error_code is null or char_length(error_code) between 1 and 80),
    uploaded_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (device_id, camera_id, source_fingerprint),
    foreign key (device_id, organization_id)
        references public.devices(id, organization_id) on delete cascade,
    foreign key (camera_id, device_id)
        references public.cameras(id, device_id) on delete cascade,
    check (char_length(source_relative_path) between 1 and 1024),
    check (source_relative_path !~ '(^/|(^|/)\.\.(/|$)|:)'),
    check (position(chr(92) in source_relative_path) = 0),
    check (position('//' in source_relative_path) = 0),
    check (char_length(original_file_name) between 1 and 255),
    check (position('/' in original_file_name) = 0),
    check (position(chr(92) in original_file_name) = 0),
    check (lower(original_file_name) like '%.mp4'),
    check (source_fingerprint ~ '^[0-9a-f]{64}$'),
    check (file_size_bytes between 1 and 10995116277760),
    check (duration_seconds is null or duration_seconds between 0 and 604800),
    check (storage_bucket = 'dfblackbox-recordings'),
    check ((sync_state = 'ready') = (uploaded_at is not null))
);

create index recordings_organization_recorded_idx
    on public.recordings (organization_id, recorded_at desc, id desc)
    where sync_state = 'ready';
create index recordings_camera_recorded_idx
    on public.recordings (camera_id, recorded_at desc, id desc)
    where sync_state = 'ready';
create index recordings_uploading_idx
    on public.recordings (updated_at)
    where sync_state <> 'ready';

create trigger recordings_set_updated_at
before update on public.recordings
for each row execute function private.set_updated_at();

alter table public.recordings enable row level security;

create policy recordings_member_select
on public.recordings for select to authenticated
using ((select private.is_organization_member(organization_id)));

revoke all on table public.recordings from anon, authenticated;
grant select on table public.recordings to authenticated;
grant all on table public.recordings to service_role;

create or replace function public.begin_device_recording_upload(
    p_device_id uuid,
    p_device_token text,
    p_camera_id uuid,
    p_source_relative_path text,
    p_original_file_name text,
    p_file_size_bytes bigint,
    p_source_fingerprint text,
    p_recorded_at timestamptz,
    p_duration_seconds numeric default null,
    p_source_last_modified_at timestamptz default null
)
returns table (
    recording_id uuid,
    sync_state text,
    storage_bucket text,
    storage_object_path text
)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_organization_id uuid;
    v_recording public.recordings%rowtype;
    v_recording_id uuid;
    v_source_path text := trim(p_source_relative_path);
    v_file_name text := trim(p_original_file_name);
    v_fingerprint text := lower(trim(p_source_fingerprint));
begin
    select device.organization_id
      into v_organization_id
      from public.devices device
     where device.id = p_device_id
       and device.registration_state = 'active'
       and device.revoked_at is null
       and device.token_hash = extensions.digest(p_device_token, 'sha256')
     for update;

    if not found then
        raise exception 'device_authentication_failed';
    end if;
    if not exists (
        select 1
          from public.cameras camera
         where camera.id = p_camera_id
           and camera.device_id = p_device_id
    ) then
        raise exception 'camera_not_found';
    end if;
    if char_length(v_source_path) not between 1 and 1024
       or v_source_path ~ '(^/|(^|/)\.\.(/|$)|:)'
       or position(chr(92) in v_source_path) > 0
       or v_source_path like '%//%' then
        raise exception 'invalid_source_relative_path';
    end if;
    if char_length(v_file_name) not between 1 and 255
       or position('/' in v_file_name) > 0
       or position(chr(92) in v_file_name) > 0
       or lower(v_file_name) not like '%.mp4' then
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
    if p_duration_seconds is not null
       and p_duration_seconds not between 0 and 604800 then
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
        v_recording_id := gen_random_uuid();
        insert into public.recordings (
            id,
            organization_id,
            device_id,
            camera_id,
            source_relative_path,
            original_file_name,
            source_fingerprint,
            source_last_modified_at,
            recorded_at,
            duration_seconds,
            file_size_bytes,
            storage_object_path
        ) values (
            v_recording_id,
            v_organization_id,
            p_device_id,
            p_camera_id,
            v_source_path,
            v_file_name,
            v_fingerprint,
            p_source_last_modified_at,
            p_recorded_at,
            p_duration_seconds,
            p_file_size_bytes,
            concat(v_organization_id::text, '/', p_camera_id::text, '/', v_recording_id::text, '.mp4')
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
       set source_relative_path = v_source_path,
           original_file_name = v_file_name,
           source_last_modified_at = coalesce(p_source_last_modified_at, recording.source_last_modified_at),
           source_last_seen_at = now(),
           duration_seconds = coalesce(p_duration_seconds, recording.duration_seconds),
           sync_state = case when recording.sync_state = 'ready' then 'ready' else 'uploading' end,
           error_code = null
     where recording.id = v_recording.id
     returning recording.* into v_recording;

    update public.devices
       set last_seen_at = now()
     where id = p_device_id;

    return query select
        v_recording.id,
        v_recording.sync_state,
        v_recording.storage_bucket,
        v_recording.storage_object_path;
end;
$$;

create or replace function public.get_device_recording_upload(
    p_device_id uuid,
    p_device_token text,
    p_recording_id uuid,
    p_file_size_bytes bigint,
    p_source_fingerprint text
)
returns table (
    recording_id uuid,
    sync_state text,
    storage_bucket text,
    storage_object_path text,
    uploaded_at timestamptz
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

    return query
    select
        recording.id,
        recording.sync_state,
        recording.storage_bucket,
        recording.storage_object_path,
        recording.uploaded_at
      from public.recordings recording
     where recording.id = p_recording_id
       and recording.device_id = p_device_id
       and recording.file_size_bytes = p_file_size_bytes
       and recording.source_fingerprint = lower(trim(p_source_fingerprint));

    if not found then
        raise exception 'recording_not_found_or_metadata_mismatch';
    end if;
end;
$$;

create or replace function public.complete_device_recording_upload(
    p_device_id uuid,
    p_device_token text,
    p_recording_id uuid,
    p_file_size_bytes bigint,
    p_source_fingerprint text
)
returns table (recording_id uuid, sync_state text, uploaded_at timestamptz)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_recording public.recordings%rowtype;
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

    select recording.*
      into v_recording
      from public.recordings recording
     where recording.id = p_recording_id
       and recording.device_id = p_device_id
     for update;

    if not found then
        raise exception 'recording_not_found';
    end if;
    if v_recording.file_size_bytes <> p_file_size_bytes
       or v_recording.source_fingerprint <> lower(trim(p_source_fingerprint)) then
        raise exception 'recording_metadata_mismatch';
    end if;

    if v_recording.sync_state <> 'ready' then
        update public.recordings recording
           set sync_state = 'ready',
               uploaded_at = now(),
               error_code = null
         where recording.id = p_recording_id
         returning recording.* into v_recording;
    end if;

    return query select v_recording.id, v_recording.sync_state, v_recording.uploaded_at;
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

    select device.organization_id
      into v_organization_id
      from public.cameras camera
      join public.devices device on device.id = camera.device_id
     where camera.id = p_camera_id;

    if not found then
        raise exception 'camera_not_found';
    end if;
    if not private.is_organization_member(v_organization_id) then
        raise exception 'recording_access_denied';
    end if;

    return query
    select
        recording.id,
        recording.camera_id,
        recording.original_file_name,
        recording.recorded_at,
        recording.duration_seconds,
        recording.file_size_bytes
      from public.recordings recording
     where recording.camera_id = p_camera_id
       and recording.sync_state = 'ready'
       and (
           p_cursor_recorded_at is null
           or (recording.recorded_at, recording.id) < (p_cursor_recorded_at, p_cursor_id)
       )
     order by recording.recorded_at desc, recording.id desc
     limit least(greatest(coalesce(p_limit, 51), 1), 101);
end;
$$;

create or replace function public.get_recording_media_access(p_recording_id uuid)
returns table (
    recording_id uuid,
    original_file_name text,
    storage_bucket text,
    storage_object_path text
)
language plpgsql
stable
security definer
set search_path = ''
as $$
begin
    return query
    select
        recording.id,
        recording.original_file_name,
        recording.storage_bucket,
        recording.storage_object_path
      from public.recordings recording
     where recording.id = p_recording_id
       and recording.sync_state = 'ready'
       and private.is_organization_member(recording.organization_id);
end;
$$;

revoke execute on function public.begin_device_recording_upload(
    uuid, text, uuid, text, text, bigint, text, timestamptz, numeric, timestamptz
) from public, anon, authenticated;
revoke execute on function public.complete_device_recording_upload(
    uuid, text, uuid, bigint, text
) from public, anon, authenticated;
revoke execute on function public.get_device_recording_upload(
    uuid, text, uuid, bigint, text
) from public, anon, authenticated;
grant execute on function public.begin_device_recording_upload(
    uuid, text, uuid, text, text, bigint, text, timestamptz, numeric, timestamptz
) to service_role;
grant execute on function public.complete_device_recording_upload(
    uuid, text, uuid, bigint, text
) to service_role;
grant execute on function public.get_device_recording_upload(
    uuid, text, uuid, bigint, text
) to service_role;

revoke execute on function public.list_camera_recordings(uuid, integer, timestamptz, uuid)
    from public, anon;
revoke execute on function public.get_recording_media_access(uuid)
    from public, anon;
grant execute on function public.list_camera_recordings(uuid, integer, timestamptz, uuid)
    to authenticated, service_role;
grant execute on function public.get_recording_media_access(uuid)
    to authenticated, service_role;

commit;
