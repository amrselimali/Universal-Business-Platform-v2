-- 0044 — سجل النسخ الاحتياطي: يُكتب من وظيفة cron خارجية (pg_dump) وتقرأه الواجهة فقط
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE backup_log (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    started_at       timestamptz NOT NULL DEFAULT now(),
    finished_at      timestamptz,
    status           varchar(20) NOT NULL DEFAULT 'RUNNING'
                     CHECK (status IN ('RUNNING', 'SUCCESS', 'FAILED')),
    kind             varchar(20) NOT NULL DEFAULT 'FULL'
                     CHECK (kind IN ('FULL', 'SCHEMA', 'DATA')),
    file_name        varchar(255),
    size_bytes       bigint CHECK (size_bytes IS NULL OR size_bytes >= 0),
    duration_seconds integer CHECK (duration_seconds IS NULL OR duration_seconds >= 0),
    error            text,
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ck_backup_log_finished
        CHECK (status = 'RUNNING' OR finished_at IS NOT NULL)
);

CREATE INDEX ix_backup_log_started ON backup_log (started_at DESC);

-- تُستدعى من cron السيرفر بعد pg_dump (الواجهة لا تنفّذ أوامر نظام).
CREATE OR REPLACE FUNCTION fn_record_backup(
    p_status varchar, p_kind varchar, p_file varchar, p_size bigint,
    p_duration integer, p_error text DEFAULT NULL) RETURNS uuid AS $$
    INSERT INTO backup_log (status, kind, file_name, size_bytes, duration_seconds, error,
                            finished_at)
    VALUES (p_status, p_kind, p_file, p_size, p_duration, p_error, now())
    RETURNING id;
$$ LANGUAGE sql;

COMMENT ON TABLE backup_log IS
    'سجل نسخ احتياطي للقراءة من الواجهة؛ الإدخال من وظيفة cron خارجية عبر fn_record_backup.';
