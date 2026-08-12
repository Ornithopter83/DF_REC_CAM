begin;

create extension if not exists pgcrypto with schema extensions;

create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated, service_role;

create table public.organizations (
    id uuid primary key default gen_random_uuid(),
    name text not null check (char_length(name) between 1 and 120),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table public.organization_members (
    organization_id uuid not null references public.organizations(id) on delete cascade,
    user_id uuid not null references auth.users(id) on delete cascade,
    role text not null check (role in ('owner', 'admin', 'viewer')),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    primary key (organization_id, user_id)
);

create table public.sites (
    id uuid primary key default gen_random_uuid(),
    organization_id uuid not null references public.organizations(id) on delete cascade,
    name text not null check (char_length(name) between 1 and 120),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (id, organization_id),
    unique (organization_id, name)
);

create table public.devices (
    id uuid primary key default gen_random_uuid(),
    organization_id uuid not null references public.organizations(id) on delete restrict,
    site_id uuid not null,
    installation_id text not null unique check (char_length(installation_id) between 16 and 128),
    display_name text not null check (char_length(display_name) between 1 and 120),
    app_version text not null check (char_length(app_version) between 1 and 64),
    registration_state text not null default 'approved'
        check (registration_state in ('approved', 'active', 'revoked')),
    storage_state text not null default 'pending'
        check (storage_state in ('pending', 'ready', 'storage_error')),
    token_hash bytea,
    token_issued_at timestamptz,
    last_seen_at timestamptz,
    revoked_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    foreign key (site_id, organization_id)
        references public.sites(id, organization_id) on delete restrict,
    check ((registration_state = 'revoked') = (revoked_at is not null))
);

create table public.cameras (
    id uuid primary key default gen_random_uuid(),
    device_id uuid not null references public.devices(id) on delete cascade,
    camera_type text not null check (camera_type in ('IP', 'USB')),
    display_name text not null default 'Camera 1'
        check (char_length(display_name) between 1 and 120),
    connection_state text not null default 'unknown'
        check (connection_state in ('unknown', 'connected', 'disconnected')),
    storage_state text not null default 'pending'
        check (storage_state in ('pending', 'ready', 'storage_error')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table public.device_claims (
    id uuid primary key default gen_random_uuid(),
    claim_code_hash bytea not null unique,
    installation_id text not null check (char_length(installation_id) between 16 and 128),
    app_version text not null check (char_length(app_version) between 1 and 64),
    camera_type text not null check (camera_type in ('IP', 'USB')),
    status text not null default 'pending'
        check (status in ('pending', 'approved', 'rejected', 'expired')),
    expires_at timestamptz not null,
    approved_by uuid references auth.users(id) on delete set null,
    approved_at timestamptz,
    rejected_at timestamptz,
    token_delivered_at timestamptz,
    organization_id uuid references public.organizations(id) on delete restrict,
    site_id uuid,
    device_id uuid references public.devices(id) on delete restrict,
    camera_id uuid references public.cameras(id) on delete restrict,
    nas_relative_path text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    foreign key (site_id, organization_id)
        references public.sites(id, organization_id) on delete restrict,
    check (expires_at > created_at),
    check (nas_relative_path is null or nas_relative_path !~ '(^|[\\/])\.\.([\\/]|$)')
);

create table public.device_provisioning_reports (
    id bigint generated always as identity primary key,
    device_id uuid not null references public.devices(id) on delete cascade,
    camera_id uuid references public.cameras(id) on delete set null,
    registration_state text not null check (registration_state in ('active', 'revoked')),
    storage_state text not null check (storage_state in ('ready', 'storage_error')),
    error_code text check (error_code is null or char_length(error_code) between 1 and 80),
    created_at timestamptz not null default now()
);

create unique index device_claims_one_pending_per_installation
    on public.device_claims (installation_id)
    where status = 'pending';
create index organization_members_user_id_idx
    on public.organization_members (user_id, organization_id)
    where is_active;
create index sites_organization_id_idx on public.sites (organization_id);
create index devices_organization_id_idx on public.devices (organization_id);
create index devices_site_id_idx on public.devices (site_id);
create index cameras_device_id_idx on public.cameras (device_id);
create index device_claims_expires_at_idx
    on public.device_claims (expires_at)
    where status = 'pending';
create index device_provisioning_reports_device_id_idx
    on public.device_provisioning_reports (device_id, created_at desc);

create or replace function private.set_updated_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

create trigger organizations_set_updated_at
before update on public.organizations
for each row execute function private.set_updated_at();
create trigger organization_members_set_updated_at
before update on public.organization_members
for each row execute function private.set_updated_at();
create trigger sites_set_updated_at
before update on public.sites
for each row execute function private.set_updated_at();
create trigger devices_set_updated_at
before update on public.devices
for each row execute function private.set_updated_at();
create trigger cameras_set_updated_at
before update on public.cameras
for each row execute function private.set_updated_at();
create trigger device_claims_set_updated_at
before update on public.device_claims
for each row execute function private.set_updated_at();

create or replace function private.is_organization_member(p_organization_id uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1
        from public.organization_members member
        where member.organization_id = p_organization_id
          and member.user_id = (select auth.uid())
          and member.is_active
    );
$$;

create or replace function private.is_organization_admin(p_organization_id uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1
        from public.organization_members member
        where member.organization_id = p_organization_id
          and member.user_id = (select auth.uid())
          and member.role in ('owner', 'admin')
          and member.is_active
    );
$$;

revoke execute on function private.set_updated_at() from public, anon, authenticated;
revoke execute on function private.is_organization_member(uuid) from public, anon;
revoke execute on function private.is_organization_admin(uuid) from public, anon;
grant execute on function private.is_organization_member(uuid) to authenticated, service_role;
grant execute on function private.is_organization_admin(uuid) to authenticated, service_role;

alter table public.organizations enable row level security;
alter table public.organization_members enable row level security;
alter table public.sites enable row level security;
alter table public.devices enable row level security;
alter table public.cameras enable row level security;
alter table public.device_claims enable row level security;
alter table public.device_provisioning_reports enable row level security;

create policy organizations_member_select
on public.organizations for select to authenticated
using ((select private.is_organization_member(id)));

create policy organization_members_visible_select
on public.organization_members for select to authenticated
using (
    user_id = (select auth.uid())
    or (select private.is_organization_admin(organization_id))
);

create policy sites_member_select
on public.sites for select to authenticated
using ((select private.is_organization_member(organization_id)));

create policy devices_member_select
on public.devices for select to authenticated
using ((select private.is_organization_member(organization_id)));

create policy cameras_member_select
on public.cameras for select to authenticated
using (
    device_id in (
        select device.id
        from public.devices device
        where device.organization_id in (
            select member.organization_id
            from public.organization_members member
            where member.user_id = (select auth.uid())
              and member.is_active
        )
    )
);

create policy provisioning_reports_member_select
on public.device_provisioning_reports for select to authenticated
using (
    device_id in (
        select device.id
        from public.devices device
        where device.organization_id in (
            select member.organization_id
            from public.organization_members member
            where member.user_id = (select auth.uid())
              and member.is_active
        )
    )
);

revoke all on table public.organizations from anon, authenticated;
revoke all on table public.organization_members from anon, authenticated;
revoke all on table public.sites from anon, authenticated;
revoke all on table public.devices from anon, authenticated;
revoke all on table public.cameras from anon, authenticated;
revoke all on table public.device_claims from anon, authenticated;
revoke all on table public.device_provisioning_reports from anon, authenticated;
grant select on table public.organizations to authenticated;
grant select on table public.organization_members to authenticated;
grant select on table public.sites to authenticated;
grant select on table public.devices to authenticated;
grant select on table public.cameras to authenticated;
grant select on table public.device_provisioning_reports to authenticated;
grant all on table public.organizations to service_role;
grant all on table public.organization_members to service_role;
grant all on table public.sites to service_role;
grant all on table public.devices to service_role;
grant all on table public.cameras to service_role;
grant all on table public.device_claims to service_role;
grant all on table public.device_provisioning_reports to service_role;
grant usage, select on sequence public.device_provisioning_reports_id_seq to service_role;

create or replace function public.create_device_claim(
    p_claim_code text,
    p_app_version text,
    p_installation_id text,
    p_camera_type text,
    p_expires_at timestamptz default (now() + interval '5 minutes')
)
returns table (claim_id uuid, expires_at timestamptz)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_claim_id uuid;
begin
    if char_length(trim(p_claim_code)) not between 6 and 32 then
        raise exception 'invalid_claim_code';
    end if;
    if char_length(trim(p_installation_id)) not between 16 and 128 then
        raise exception 'invalid_installation_id';
    end if;
    if p_camera_type not in ('IP', 'USB') then
        raise exception 'invalid_camera_type';
    end if;
    if p_expires_at <= now() or p_expires_at > now() + interval '30 minutes' then
        raise exception 'invalid_expiration';
    end if;

    update public.device_claims claim
       set status = 'expired'
     where claim.installation_id = trim(p_installation_id)
       and claim.status = 'pending';

    insert into public.device_claims (
        claim_code_hash,
        installation_id,
        app_version,
        camera_type,
        expires_at
    ) values (
        extensions.digest(upper(trim(p_claim_code)), 'sha256'),
        trim(p_installation_id),
        trim(p_app_version),
        p_camera_type,
        p_expires_at
    )
    returning id into v_claim_id;

    return query select v_claim_id, p_expires_at;
end;
$$;

create or replace function public.approve_device_claim(
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
              and device.revoked_at is null
        ) then
            raise exception 'installation_already_registered';
        end if;
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
    end if;

    v_relative_path := concat(
        'DFBlackbox/',
        p_organization_id::text, '/',
        p_site_id::text, '/',
        v_device_id::text, '/',
        v_camera_id::text
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

create or replace function public.reject_device_claim(
    p_claim_code text,
    p_organization_id uuid
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
    if not private.is_organization_admin(p_organization_id) then
        raise exception 'rejection_not_authorized';
    end if;

    update public.device_claims claim
       set status = 'rejected',
           rejected_at = now()
     where claim.claim_code_hash = extensions.digest(upper(trim(p_claim_code)), 'sha256')
       and claim.status = 'pending'
       and claim.expires_at > now();
    return found;
end;
$$;

create or replace function public.consume_device_claim(
    p_claim_id uuid
)
returns table (
    status text,
    device_id uuid,
    camera_id uuid,
    device_token text,
    nas_relative_path text
)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_claim public.device_claims%rowtype;
    v_token text;
    v_has_token boolean;
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
        return query select v_claim.status, null::uuid, null::uuid, null::text, null::text;
        return;
    end if;

    if v_claim.token_delivered_at is null then
        select device.token_hash is not null into v_has_token
        from public.devices device
        where device.id = v_claim.device_id
        for update;

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
        v_claim.nas_relative_path;
end;
$$;

create or replace function public.report_device_provisioning(
    p_device_id uuid,
    p_device_token text,
    p_registration_state text,
    p_storage_state text,
    p_error_code text default null
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_camera_id uuid;
begin
    if p_registration_state <> 'active' then
        raise exception 'invalid_registration_state';
    end if;
    if p_storage_state not in ('ready', 'storage_error') then
        raise exception 'invalid_storage_state';
    end if;
    if p_storage_state = 'ready' and p_error_code is not null then
        raise exception 'unexpected_error_code';
    end if;

    update public.devices device
       set registration_state = 'active',
           storage_state = p_storage_state,
           last_seen_at = now()
     where device.id = p_device_id
       and device.revoked_at is null
       and device.token_hash = extensions.digest(p_device_token, 'sha256');

    if not found then
        raise exception 'device_authentication_failed';
    end if;

    select camera.id into v_camera_id
    from public.cameras camera
    where camera.device_id = p_device_id
    order by camera.created_at
    limit 1;

    update public.cameras
       set storage_state = p_storage_state
     where id = v_camera_id;

    insert into public.device_provisioning_reports (
        device_id,
        camera_id,
        registration_state,
        storage_state,
        error_code
    ) values (
        p_device_id,
        v_camera_id,
        p_registration_state,
        p_storage_state,
        p_error_code
    );

    return true;
end;
$$;

revoke execute on function public.create_device_claim(text, text, text, text, timestamptz)
    from public, anon, authenticated;
revoke execute on function public.approve_device_claim(text, uuid, uuid, text)
    from public, anon;
revoke execute on function public.reject_device_claim(text, uuid)
    from public, anon;
revoke execute on function public.consume_device_claim(uuid)
    from public, anon, authenticated;
revoke execute on function public.report_device_provisioning(uuid, text, text, text, text)
    from public, anon, authenticated;

grant execute on function public.create_device_claim(text, text, text, text, timestamptz)
    to service_role;
grant execute on function public.approve_device_claim(text, uuid, uuid, text)
    to authenticated, service_role;
grant execute on function public.reject_device_claim(text, uuid)
    to authenticated, service_role;
grant execute on function public.consume_device_claim(uuid)
    to service_role;
grant execute on function public.report_device_provisioning(uuid, text, text, text, text)
    to service_role;

commit;
