-- 0070 — Webhooks واتساب: تحقق التوكن والتوقيع، حالة التسليم، والرسائل الواردة
-- كل فرع له Verify Token وApp Secret مستقلان؛ والرسائل الواردة تُحفظ في جدول صريح.

ALTER TABLE branch_whatsapp_settings
    ADD COLUMN IF NOT EXISTS webhook_verify_token varchar(120),
    ADD COLUMN IF NOT EXISTS app_secret varchar(200);

ALTER TABLE message_outbox
    ADD COLUMN IF NOT EXISTS provider_message_id varchar(120),
    ADD COLUMN IF NOT EXISTS delivery_status varchar(20),
    ADD COLUMN IF NOT EXISTS delivery_updated_at timestamptz;

CREATE INDEX IF NOT EXISTS ix_message_outbox_provider
    ON message_outbox (provider_message_id);

CREATE TABLE IF NOT EXISTS whatsapp_inbound_messages (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id     uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    wa_message_id varchar(120) NOT NULL,
    from_phone    varchar(40) NOT NULL,
    message_type  varchar(30) NOT NULL,
    text_body     text,
    sent_at       timestamptz,
    payload       jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_whatsapp_inbound_message UNIQUE (wa_message_id)
);

CREATE INDEX IF NOT EXISTS ix_whatsapp_inbound_branch
    ON whatsapp_inbound_messages (branch_id, created_at DESC);

COMMENT ON TABLE whatsapp_inbound_messages IS
    'الرسائل الواردة من واتساب عبر Webhook (مصدر واحد لعرضها والرد عليها لاحقًا).';
