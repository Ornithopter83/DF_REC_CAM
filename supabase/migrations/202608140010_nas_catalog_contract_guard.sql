begin;

alter table public.cameras
    add column nas_relative_path text;

update public.cameras camera
   set nas_relative_path = (
       select claim.nas_relative_path
         from public.device_claims claim
        where claim.camera_id = camera.id
          and claim.status = 'approved'
          and claim.nas_relative_path is not null
        order by claim.approved_at desc nulls last, claim.created_at desc
        limit 1
   )
 where camera.nas_relative_path is null
   and exists (
       select 1
         from public.device_claims claim
        where claim.camera_id = camera.id
          and claim.status = 'approved'
          and claim.nas_relative_path is not null
   );

alter table public.cameras
    add constraint cameras_nas_relative_path_check check (
        nas_relative_path is null
        or (
            char_length(nas_relative_path) between 1 and 768
            and nas_relative_path !~ '(^/|(^|/)\.{1,2}(/|$)|:)'
            and position(chr(92) in nas_relative_path) = 0
            and position('//' in nas_relative_path) = 0
        )
    );

create or replace function private.sync_claim_nas_path_to_camera()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if new.status = 'approved'
       and new.camera_id is not null
       and new.nas_relative_path is not null then
        update public.cameras
           set nas_relative_path = new.nas_relative_path
         where id = new.camera_id;
    end if;
    return new;
end;
$$;

create trigger device_claims_sync_camera_nas_path
after insert or update of status, camera_id, nas_relative_path on public.device_claims
for each row execute function private.sync_claim_nas_path_to_camera();

do $$
begin
    perform *
      from public.register_device_recording_catalog(
          '11111111-1111-4111-8111-111111111111'::uuid,
          'invalid-device-token',
          '11111111-1111-4111-8111-111111111111'::uuid,
          'DFBlackbox/MyCompany/Camera1/recordings/contract-test.mp4',
          'contract-test.mp4',
          1024,
          repeat('a', 64),
          now(),
          null,
          null
      );
    raise exception 'catalog_authentication_guard_failed';
exception
    when others then
        if sqlerrm <> 'device_authentication_failed' then
            raise;
        end if;
end;
$$;

notify pgrst, 'reload schema';

commit;
