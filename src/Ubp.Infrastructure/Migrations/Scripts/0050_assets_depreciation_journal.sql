-- 0050 — يومية الإهلاك (DEP) للأصول الثابتة
-- لا يُعدَّل أي ترحيل سابق (G1).
-- allow_manual = true لأن محرك الإهلاك ينشئ القيد عبر خدمة القيود نفسها،
-- وبقية التحقق (التوازن/الفترة/الصلاحيات) يبقى قائماً.

INSERT INTO journals (code, name_ar, name_en, journal_type, allow_manual, is_system, sort_order)
SELECT 'DEP', 'يومية الإهلاك', 'Depreciation Journal', 'general', true, true, 70
WHERE NOT EXISTS (SELECT 1 FROM journals WHERE code = 'DEP');
