-- 0034 — المرحلة 4H: متابعات العملاء وربطها بالحجوزات والمواعيد
-- -----------------------------------------------------------------------------

CREATE TABLE follow_ups (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active        boolean NOT NULL DEFAULT true,
    version          integer NOT NULL DEFAULT 1,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at       timestamptz,
    updated_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    customer_id      uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    booking_id       uuid REFERENCES bookings(id) ON DELETE SET NULL,
    appointment_id   uuid REFERENCES appointments(id) ON DELETE SET NULL,
    assigned_user_id uuid REFERENCES users(id) ON DELETE SET NULL,
    due_at           timestamptz NOT NULL,
    channel          varchar(20) NOT NULL DEFAULT 'call'
                     CHECK (channel IN ('call','whatsapp','sms','visit')),
    note             varchar(500),
    status           varchar(10) NOT NULL DEFAULT 'pending'
                     CHECK (status IN ('pending','done','cancelled')),
    completed_at     timestamptz
);

CREATE INDEX ix_followups_due ON follow_ups (status, due_at) WHERE status = 'pending';
CREATE UNIQUE INDEX ux_followups_pending_booking ON follow_ups (booking_id)
    WHERE status = 'pending' AND booking_id IS NOT NULL;

CREATE TRIGGER trg_touch_follow_ups BEFORE UPDATE ON follow_ups
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
