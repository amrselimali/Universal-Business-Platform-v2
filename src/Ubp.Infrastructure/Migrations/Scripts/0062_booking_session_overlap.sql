-- 0062 — منع تداخل مواعيد الجلسات المحجوزة لنفس الموظف
CREATE OR REPLACE FUNCTION fn_booking_session_no_overlap()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.assigned_employee_id IS NULL OR NEW.starts_at IS NULL OR NEW.ends_at IS NULL
       OR NEW.status = 'cancelled' THEN
        RETURN NEW;
    END IF;

    IF EXISTS (
        SELECT 1 FROM bookings b
        WHERE b.id <> NEW.id
          AND b.assigned_employee_id = NEW.assigned_employee_id
          AND b.status <> 'cancelled'
          AND b.starts_at IS NOT NULL AND b.ends_at IS NOT NULL
          AND b.starts_at < NEW.ends_at
          AND b.ends_at > NEW.starts_at
    ) THEN
        RAISE EXCEPTION 'BOOKING_SESSION_OVERLAP' USING ERRCODE = '23P01';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_booking_session_no_overlap ON bookings;
CREATE TRIGGER trg_booking_session_no_overlap
BEFORE INSERT OR UPDATE OF assigned_employee_id, starts_at, ends_at, status
ON bookings
FOR EACH ROW EXECUTE FUNCTION fn_booking_session_no_overlap();

CREATE INDEX IF NOT EXISTS ix_bookings_session_window
    ON bookings (assigned_employee_id, starts_at, ends_at)
    WHERE assigned_employee_id IS NOT NULL AND starts_at IS NOT NULL AND ends_at IS NOT NULL;