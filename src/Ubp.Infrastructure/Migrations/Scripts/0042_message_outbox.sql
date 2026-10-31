-- 0042 — صندوق الرسائل الخارجية (Outbox) لإرسال SMS/واتساب بلا فقدان وبـ backoff
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE message_outbox (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    channel         varchar(20) NOT NULL
                    CHECK (channel IN ('SMS', 'WHATSAPP', 'EMAIL', 'PUSH')),
    recipient       varchar(200) NOT NULL,
    template_key    varchar(120),
    body            text NOT NULL,
    payload         jsonb NOT NULL DEFAULT '{}'::jsonb,
    dedupe_key      varchar(120),
    status          varchar(20) NOT NULL DEFAULT 'PENDING'
                    CHECK (status IN ('PENDING', 'SENT', 'FAILED', 'CANCELLED')),
    attempts        smallint NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    last_error      text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    sent_at         timestamptz,
    CONSTRAINT ck_message_outbox_body CHECK (length(body) BETWEEN 1 AND 4000),
    CONSTRAINT ck_message_outbox_recipient CHECK (length(btrim(recipient)) >= 3),
    CONSTRAINT ck_message_outbox_payload CHECK (jsonb_typeof(payload) = 'object')
);

-- منع إرسال نفس الرسالة مرتين عند إعادة المحاولة من الطبقة الأعلى
CREATE UNIQUE INDEX ux_message_outbox_dedupe
    ON message_outbox (dedupe_key) WHERE dedupe_key IS NOT NULL;

CREATE INDEX ix_message_outbox_due
    ON message_outbox (next_attempt_at) WHERE status = 'PENDING';
CREATE INDEX ix_message_outbox_status_created
    ON message_outbox (status, created_at DESC);

COMMENT ON TABLE message_outbox IS
    'رسائل خارجية تُسحب من معالج خلفي؛ next_attempt_at يعمل كموعد أو قفل استئجار مؤقت.';
