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
        insert into public.cameras (device_id, camera_type)
        values (v_device_id, v_claim.camera_type)
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
        private.sanitize_nas_path_segment(v_camera_name, true)
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

create or replace function public.consume_device_claim_v2(p_claim_id uuid)
returns table (
    status text,
    device_id uuid,
    camera_id uuid,
    device_token text,
    nas_relative_path text,
    registration_name text
)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_claim public.device_claims%rowtype;
    v_token text;
    v_has_token boolean;
    v_registration_name text;
begin
    select claim.* into v_claim
    from public.device_claims claim
    where claim.id = p_claim_id
    for update;

    if not found then
        raise exception 'claim_not_found';
    end if;
    if v_claim.status = 'pending' and v_claim.expires_at <= now() then
        update public.device_claims set status = 'expired' where id = v_claim.id;
        v_claim.status := 'expired';
    end if;

    if v_claim.status <> 'approved' then
        return query select
            v_claim.status,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text;
        return;
    end if;

    select device.display_name, device.token_hash is not null
      into v_registration_name, v_has_token
    from public.devices device
    where device.id = v_claim.device_id
    for update;

    if not found or v_registration_name is null then
        raise exception 'registered_device_not_found';
    end if;

    if v_claim.token_delivered_at is null then
        if not coalesce(v_has_token, false) then
            v_token := encode(extensions.gen_random_bytes(32), 'hex');
            update public.devices
               set token_hash = extensions.digest(v_token, 'sha256'),
                   token_issued_at = now()
             where id = v_claim.device_id;
        end if;

        update public.device_claims
           set token_delivered_at = now()
         where id = v_claim.id;
    end if;

    return query select
        v_claim.status,
        v_claim.device_id,
        v_claim.camera_id,
        v_token,
        v_claim.nas_relative_path,
        v_registration_name;
end;
$$;

create or replace function public.revoke_device_registration(
    p_device_id uuid,
    p_device_token text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    update public.devices device
       set registration_state = 'revoked',
           revoked_at = now(),
           token_hash = null,
           token_issued_at = null,
           last_seen_at = now()
     where device.id = p_device_id
       and device.revoked_at is null
       and device.token_hash = extensions.digest(p_device_token, 'sha256');

    if not found then
        raise exception 'device_authentication_failed';
    end if;

    update public.cameras camera
       set connection_state = 'disconnected'
     where camera.device_id = p_device_id;

    return true;
end;
$$;

revoke execute on function public.approve_device_claim_v2(text, uuid, uuid, text)
    from public, anon;
revoke execute on function public.consume_device_claim_v2(uuid)
    from public, anon, authenticated;
revoke execute on function public.revoke_device_registration(uuid, text)
    from public, anon, authenticated;
grant execute on function public.approve_device_claim_v2(text, uuid, uuid, text)
    to authenticated, service_role;
grant execute on function public.consume_device_claim_v2(uuid)
    to service_role;
grant execute on function public.revoke_device_registration(uuid, text)
    to service_role;
commit;
