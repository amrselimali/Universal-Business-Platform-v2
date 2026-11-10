-- 0094 — كود الموظف تسلسلي لكل فرع (بدءًا من 1) بدلاً من قيد فريد عام
-- ---------------------------------------------------------------------------
-- الموظف يُكوَّد آلياً داخل فرعه بدءاً من 1، لذا يصبح القيد الفريد
-- (الفرع، الكود) بدلاً من (الكود) وحده. موظفو الشركة (branch_id IS NULL)
-- يشتركون في نطاق واحد ممثَّل بقيمة UUID صفرية في الفهرس.

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ux_employees_code'
               AND conrelid = 'employees'::regclass) THEN
        ALTER TABLE employees DROP CONSTRAINT ux_employees_code;
    END IF;
END $$;

DROP INDEX IF EXISTS ux_employees_code;

CREATE UNIQUE INDEX IF NOT EXISTS ux_employees_branch_code
    ON employees (COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid), code);

COMMENT ON INDEX ux_employees_branch_code IS
    'كود الموظف فريد داخل فرعه فقط؛ التكويد تسلسلي آلي لكل فرع بدءاً من 1 (0094)';
