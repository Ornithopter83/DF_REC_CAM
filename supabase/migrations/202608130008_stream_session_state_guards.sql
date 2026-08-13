begin;

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
declare
    v_now timestamptz := now();
    v_current_state text;
    v_lease_until timestamptz;
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

    select stream.state, stream.viewer_lease_until
      into v_current_state, v_lease_until
      from public.camera_streams stream
     where stream.camera_id = p_camera_id
       and stream.device_id = p_device_id
     for update;

    if not found then
        raise exception 'stream_not_found';
    end if;

    if coalesce(v_lease_until, '-infinity'::timestamptz) <= v_now then
        update public.camera_streams stream
           set state = 'idle',
               error_code = null,
               last_stopped_at = case
                   when v_current_state = 'idle' then stream.last_stopped_at
                   else v_now
               end
         where stream.camera_id = p_camera_id
           and stream.device_id = p_device_id;
        return p_state = 'idle';
    end if;

    -- An idle report belongs to a command that observed no active viewer lease.
    -- If a viewer renewed the lease while that report was in flight, keep the
    -- newer requested/publishing state instead of stopping the new session.
    if p_state = 'idle' then
        return false;
    end if;

    update public.camera_streams stream
       set state = p_state,
           error_code = case
               when p_state = 'error'
                   then left(coalesce(nullif(trim(p_error_code), ''), 'stream_publish_failed'), 80)
               else null
           end,
           last_started_at = case
               when p_state = 'publishing' then v_now
               else stream.last_started_at
           end
     where stream.camera_id = p_camera_id
       and stream.device_id = p_device_id;
    return true;
end;
$$;

revoke execute on function public.report_camera_stream_state(uuid, text, uuid, text, text)
    from public, anon, authenticated;
grant execute on function public.report_camera_stream_state(uuid, text, uuid, text, text)
    to service_role;

commit;
