-- Calories Tracker: one JSON document per user, readable and writable only by its owner.
-- Run once in Supabase: SQL Editor -> New query -> paste -> Run.

create table if not exists public.user_data (
    user_id    uuid primary key default auth.uid() references auth.users (id) on delete cascade,
    data       jsonb not null,
    updated_at timestamptz not null default now()
);

-- Row Level Security: the public site + public key can only ever reach the signed-in user's own row.
alter table public.user_data enable row level security;

drop policy if exists "user_data_select_own" on public.user_data;
drop policy if exists "user_data_insert_own" on public.user_data;
drop policy if exists "user_data_update_own" on public.user_data;
drop policy if exists "user_data_delete_own" on public.user_data;

create policy "user_data_select_own" on public.user_data
    for select to authenticated using (auth.uid() = user_id);

create policy "user_data_insert_own" on public.user_data
    for insert to authenticated with check (auth.uid() = user_id);

create policy "user_data_update_own" on public.user_data
    for update to authenticated using (auth.uid() = user_id) with check (auth.uid() = user_id);

create policy "user_data_delete_own" on public.user_data
    for delete to authenticated using (auth.uid() = user_id);

-- Anonymous visitors get nothing; signed-in users go through the policies above.
revoke all on public.user_data from anon;
grant select, insert, update, delete on public.user_data to authenticated;
