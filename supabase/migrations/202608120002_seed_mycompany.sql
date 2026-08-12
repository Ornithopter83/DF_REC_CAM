begin;

do $$
declare
    v_owner_id constant uuid := '3e252f6a-1e1b-4b8b-8987-ea01514ff923';
    v_organization_id constant uuid := '6e434a58-c61c-48f7-a59d-143e3356c99a';
    v_site_id constant uuid := '767138e6-8d72-49c2-b8db-a7cb9cd56dab';
begin
    if not exists (select 1 from auth.users where id = v_owner_id) then
        raise exception 'Supabase Auth owner user does not exist: %', v_owner_id;
    end if;

    insert into public.organizations (id, name)
    values (v_organization_id, 'MyCompany')
    on conflict (id) do update
       set name = excluded.name;

    insert into public.organization_members (
        organization_id,
        user_id,
        role,
        is_active
    ) values (
        v_organization_id,
        v_owner_id,
        'owner',
        true
    )
    on conflict (organization_id, user_id) do update
       set role = excluded.role,
           is_active = excluded.is_active;

    insert into public.sites (id, organization_id, name)
    values (v_site_id, v_organization_id, 'MyComputer')
    on conflict (id) do update
       set organization_id = excluded.organization_id,
           name = excluded.name;
end;
$$;

commit;
