-- 0089 — مفضلة الشاشات لكل مستخدم (اختصارات الصفحة الرئيسية)
CREATE TABLE IF NOT EXISTS user_favorite_screens (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    screen_code varchar(40) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_user_favorite_screens UNIQUE (user_id, screen_code)
);
CREATE INDEX IF NOT EXISTS ix_user_favorite_screens_user
    ON user_favorite_screens (user_id);
COMMENT ON TABLE user_favorite_screens IS
    'شاشات مختارة كمفضلة لكل مستخدم لعرضها في الصفحة الرئيسية — 0089.';
