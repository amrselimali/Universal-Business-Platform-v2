-- 0040 — الإشعارات الداخلية
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE notifications (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     uuid REFERENCES users(id) ON DELETE CASCADE,
    role_id     uuid REFERENCES roles(id) ON DELETE CASCADE,
    title_key   varchar(120) NOT NULL,
    body        text,
    link_url    varchar(300),
    severity    varchar(10)  NOT NULL DEFAULT 'INFO'
                CHECK (severity IN ('INFO', 'SUCCESS', 'WARNING', 'ERROR')),
    payload     jsonb        NOT NULL DEFAULT '{}'::jsonb,
    read_at     timestamptz,
    read_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    -- إشعار لمستخدم واحد أو لدور واحد، لا الاثنين ولا دونهما
    CONSTRAINT ck_notifications_target CHECK ((user_id IS NULL) <> (role_id IS NULL)),
    CONSTRAINT ck_notifications_body CHECK (body IS NULL OR length(body) <= 4000),
    CONSTRAINT ck_notifications_payload CHECK (jsonb_typeof(payload) = 'object')
);

CREATE INDEX ix_notifications_user_unread
    ON notifications (user_id, created_at DESC) WHERE read_at IS NULL;
CREATE INDEX ix_notifications_role_unread
    ON notifications (role_id, created_at DESC) WHERE read_at IS NULL;
CREATE INDEX ix_notifications_user_created
    ON notifications (user_id, created_at DESC);

COMMENT ON TABLE notifications IS
    'إشعارات داخلية موجهة لمستخدم أو لدور؛ القراءة لكل مستخدم على حدة عبر read_at/read_by.';
