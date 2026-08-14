begin;

create function public.get_device_nas_upload_scope(
    p_device_id uuid,
    p_device_token text,
    p_camera_id uuid
)
returns table (
    device_id uuid,
    camera_id uuid,
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
    select device.id,
           camera.id,
           location.id,
           location.gateway_base_url,
           trim(both '/' from camera.nas_relative_path)
      from public.devices device
      join public.cameras camera
        on camera.device_id = device.id
       and camera.id = p_camera_id
      join public.nas_locations location
        on location.id = camera.nas_location_id
       and location.is_active
     where device.id = p_device_id
       and device.registration_state = 'active'
       and device.revoked_at is null
       and camera.nas_relative_path is not null;

    if not found then
        raise exception 'camera_nas_upload_scope_not_found';
    end if;
end;
$$;

revoke execute on function public.get_device_nas_upload_scope(uuid, text, uuid)
    from public, anon, authenticated;
grant execute on function public.get_device_nas_upload_scope(uuid, text, uuid)
    to service_role;

notify pgrst, 'reload schema';

commit;
