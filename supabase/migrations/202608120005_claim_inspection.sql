begin;

create or replace function public.inspect_device_claim(p_claim_code text)
returns table (status text, expires_at timestamptz)
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_claim public.device_claims%rowtype;
begin
    if (select auth.uid()) is null then
        raise exception 'authentication_required';
    end if;

    select claim.* into v_claim
    from public.device_claims claim
    where claim.claim_code_hash = extensions.digest(upper(trim(p_claim_code)), 'sha256')
    for update;

    if not found then
        return query select 'not_found'::text, null::timestamptz;
        return;
    end if;

    if v_claim.status = 'pending' and v_claim.expires_at <= now() then
        update public.device_claims
           set status = 'expired'
         where id = v_claim.id;
        v_claim.status := 'expired';
    end if;

    return query select v_claim.status, v_claim.expires_at;
end;
$$;

revoke execute on function public.inspect_device_claim(text)
    from public, anon;
grant execute on function public.inspect_device_claim(text)
    to authenticated, service_role;

commit;
