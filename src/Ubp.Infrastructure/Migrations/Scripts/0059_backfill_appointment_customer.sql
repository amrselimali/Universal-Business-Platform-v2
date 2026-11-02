-- 0059 — استكمال عميل المواعيد القديمة المرتبطة بحجز
UPDATE appointments a
   SET customer_id = b.customer_id
  FROM bookings b
 WHERE a.booking_id = b.id
   AND a.customer_id IS NULL;
