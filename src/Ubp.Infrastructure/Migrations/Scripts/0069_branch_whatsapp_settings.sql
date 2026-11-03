-- 0069 — إعدادات واتساب مستقلة لكل فرع (Meta Cloud API)
-- كل فرع له رقمه/معرّفه/توكّنه؛ عند غياب إعدادات الفرع تُستخدم إعدادات WhatsApp العامة.

CREATE TABLE IF NOT EXISTS branch_whatsapp_settings (
    branch_id            uuid PRIMARY KEY REFERENCES branches(id) ON DELETE CASCADE,
    is_enabled           boolean NOT NULL DEFAULT false,
    api_url              varchar(300) NOT NULL DEFAULT 'https://graph.facebook.com/v20.0',
    phone_number_id      varchar(60),
    messaging_account_id varchar(60),
    access_token         varchar(600),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    updated_by           uuid REFERENCES users(id) ON DELETE SET NULL
);

COMMENT ON TABLE branch_whatsapp_settings IS
    'إعدادات واتساب لكل فرع: التفعيل وعنوان الـAPI ومعرّف رقم الهاتف ومعرّف الحساب والتوكن.';
