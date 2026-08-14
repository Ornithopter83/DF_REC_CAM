begin;

create or replace function public.approve_device_claim_v2(
    p_claim_code text,
    p_organization_id uuid,
    p_site_id uuid,
    p_device_name text default null
)
returns table (device_id uuid, camera_id uuid, nas_relative_path text)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_claim public.device_claims%rowtype;
    v_device_id uuid;
    v_camera_id uuid;
    v_organization_name text;
    v_camera_name text;
    v_relative_path text;
begin
    if (select auth.uid()) is null then
        raise exception 'authentication_required';
    end if;
    if not private.is_organization_admin(p_organization_id) then
        raise exception 'approval_not_authorized';
    end if;
    if not exists (
        select 1 from public.sites site
        where site.id = p_site_id
          and site.organization_id = p_organization_id
    ) then
        raise exception 'invalid_site';
    end if;

    select claim.* into v_claim
    from public.device_claims claim
    where claim.claim_code_hash = extensions.digest(upper(trim(p_claim_code)), 'sha256')
    for update;

    if not found then
        raise exception 'claim_not_found';
    end if;
    if v_claim.status = 'approved' then
        if v_claim.organization_id <> p_organization_id or v_claim.site_id <> p_site_id then
            raise exception 'claim_already_approved_elsewhere';
        end if;
        return query select v_claim.device_id, v_claim.camera_id, v_claim.nas_relative_path;
        return;
    end if;
    if v_claim.status <> 'pending' then
        raise exception 'claim_not_pending';
    end if;
    if v_claim.expires_at <= now() then
        update public.device_claims set status = 'expired' where id = v_claim.id;
        raise exception 'claim_expired';
    end if;

    select device.id into v_device_id
    from public.devices device
    where device.installation_id = v_claim.installation_id
    for update;

    if found then
        if not exists (
            select 1 from public.devices device
            where device.id = v_device_id
              and device.organization_id = p_organization_id
              and device.site_id = p_site_id
        ) then
            raise exception 'installation_already_registered';
        end if;

        update public.devices
           set display_name = coalesce(nullif(trim(p_device_name), ''), display_name),
               app_version = v_claim.app_version,
               registration_state = 'approved',
               storage_state = 'pending',
               token_hash = null,
               token_issued_at = null,
               revoked_at = null
         where id = v_device_id;

        select camera.id into v_camera_id
        from public.cameras camera
        where camera.device_id = v_device_id
        order by camera.created_at
        limit 1;
    else
        insert into public.devices (
            organization_id,
            site_id,
            installation_id,
            display_name,
            app_version
        ) values (
            p_organization_id,
            p_site_id,
            v_claim.installation_id,
            coalesce(nullif(trim(p_device_name), ''), 'DFBlackbox Device'),
            v_claim.app_version
        ) returning id into v_device_id;
    end if;

    if v_camera_id is null then
        select device.display_name into strict v_camera_name
        from public.devices device
        where device.id = v_device_id;

        insert into public.cameras (device_id, camera_type, display_name)
        values (v_device_id, v_claim.camera_type, v_camera_name)
        returning id into v_camera_id;
    else
        update public.cameras
           set camera_type = v_claim.camera_type,
               connection_state = 'unknown',
               storage_state = 'pending'
         where id = v_camera_id;
    end if;

    select organization.name into strict v_organization_name
    from public.organizations organization
    where organization.id = p_organization_id;

    select camera.display_name into strict v_camera_name
    from public.cameras camera
    where camera.id = v_camera_id;

    v_relative_path := concat(
        'DFBlackbox/',
        private.sanitize_nas_path_segment(v_organization_name), '/',
        private.sanitize_nas_path_segment(v_camera_name)
    );

    update public.device_claims
       set status = 'approved',
           approved_by = (select auth.uid()),
           approved_at = now(),
           organization_id = p_organization_id,
           site_id = p_site_id,
           device_id = v_device_id,
           camera_id = v_camera_id,
           nas_relative_path = v_relative_path
     where id = v_claim.id;

    return query select v_device_id, v_camera_id, v_relative_path;
end;
$$;

commit;
