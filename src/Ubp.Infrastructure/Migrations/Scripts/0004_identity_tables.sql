-- 0004 — الهوية والصلاحيات: المستخدمون، الجلسات، الأدوار، الصلاحيات
-- --------------------------------------------------------------------

CREATE TABLE users (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name            varchar(255),
    username        varchar(150) NOT NULL,
    password_hash   varchar(255) NOT NULL,
    is_active       boolean      NOT NULL DEFAULT true,
    language        varchar(10)  NOT NULL DEFAULT 'ar' CHECK (language IN ('ar','en')),
    is_admin        boolean      NOT NULL DEFAULT false,
    is_super_admin  boolean      NOT NULL DEFAULT false,
    failed_attempts smallint     NOT NULL DEFAULT 0 CHECK (failed_attempts >= 0),
    locked_until    timestamptz,
    last_login_at   timestamptz,
    search_text     text GENERATED ALWAYS AS (
        normalize_search(coalesce(username,'') || ' ' || coalesce(name,''))
    ) STORED,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now(),
    version         integer      NOT NULL DEFAULT 1,
    CONSTRAINT ck_users_super_admin_is_admin CHECK (NOT is_super_admin OR is_admin)
);

CREATE UNIQUE INDEX ux_users_username_lower ON users (lower(username));

CREATE TABLE refresh_tokens (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash varchar(255) NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    replaced_by uuid REFERENCES refresh_tokens(id) ON DELETE SET NULL,
    created_ip inet,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_refresh_tokens_hash UNIQUE (token_hash)
);

-- جلسة السيرفر: الفرع الحالي والانتهاء بالخمول يُخزَّنان على السيرفر وليس في المتصفح
CREATE TABLE user_sessions (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id          uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    branch_id        uuid REFERENCES branches(id) ON DELETE SET NULL,
    refresh_token_id uuid REFERENCES refresh_tokens(id) ON DELETE SET NULL,
    ip_address       inet,
    last_seen_at     timestamptz NOT NULL DEFAULT now(),
    expires_at       timestamptz NOT NULL,
    revoked_at       timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE user_branches (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    branch_id  uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    is_default boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_user_branches UNIQUE (user_id, branch_id)
);

CREATE UNIQUE INDEX ux_user_branches_default ON user_branches (user_id) WHERE is_default;

CREATE TABLE user_warehouses (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    warehouse_id uuid NOT NULL REFERENCES warehouses(id) ON DELETE CASCADE,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_user_warehouses UNIQUE (user_id, warehouse_id)
);

CREATE TABLE roles (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(50)  NOT NULL,
    name_ar    varchar(255) NOT NULL,
    name_en    varchar(255),
    is_active  boolean      NOT NULL DEFAULT true,
    created_at timestamptz  NOT NULL DEFAULT now(),
    updated_at timestamptz  NOT NULL DEFAULT now(),
    version    integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_roles_code UNIQUE (code)
);

CREATE TABLE role_permissions (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id     uuid NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    screen_code varchar(100) NOT NULL,
    action_code varchar(100) NOT NULL,
    is_allowed  boolean NOT NULL DEFAULT true,
    scope       smallint NOT NULL DEFAULT 1 CHECK (scope IN (0,1,2)),
    CONSTRAINT ux_role_permissions UNIQUE (role_id, screen_code, action_code)
);

CREATE TABLE user_roles (
    id      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id uuid NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    CONSTRAINT ux_user_roles UNIQUE (user_id, role_id)
);

CREATE TABLE user_permissions (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    screen_code varchar(100) NOT NULL,
    action_code varchar(100) NOT NULL,
    is_allowed  boolean NOT NULL,
    scope       smallint NOT NULL DEFAULT 1 CHECK (scope IN (0,1,2)),
    CONSTRAINT ux_user_permissions UNIQUE (user_id, screen_code, action_code)
);
