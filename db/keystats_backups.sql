-- ============================================================
-- keyStats 每日备份表（Supabase · 坑货雷达项目）
-- 用法：Supabase 控制台 → SQL Editor → 全选粘贴 → Run（只跑一次）
-- ============================================================

create table if not exists keystats_backups (
  id                   bigserial primary key,
  team                 text        not null,
  device_id            text        not null,
  backup_date          date        not null,
  payload_gzip_base64  text        not null,
  app_version          text,
  created_at           timestamptz default now(),
  unique (team, device_id, backup_date)
);

create index if not exists keystats_backups_lookup
  on keystats_backups(team, backup_date desc);

alter table keystats_backups enable row level security;

drop policy if exists anon_rw_keystats_backups on keystats_backups;
create policy anon_rw_keystats_backups on keystats_backups
  for all to anon
  using (team = '386875064')
  with check (team = '386875064');
