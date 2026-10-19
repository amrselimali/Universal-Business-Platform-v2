# نقل المشروع إلى Repository مستقل

هذا الفرع (`v2-standalone`) يحتوي المشروع كاملاً في **جذر المستودع**، وليس داخل `platform-v2/`.
بهذا الشكل يمكن نقله إلى Repository جديد دون أي تعديل في المسارات.

---

## الطريقة 1 — من جهازك (موصى بها)

```bash
# 1) نزّل الفرع المستقل فقط
git clone --branch v2-standalone --single-branch \
  https://github.com/amrselimali/Universal-Business-Platform.git ubp-v2

cd ubp-v2

# 2) وجّهه للمستودع الجديد الفاضي
git remote set-url origin https://github.com/amrselimali/Universal-Business-Platform-v2.git

# 3) ارفع المحتوى كفرع main
git push -u origin HEAD:main
git branch -u origin/main
```

بعد نجاح الرفع، من إعدادات الـ Repo الجديد:
- **Settings → General → Default branch** اختر `main`.
- **Settings → Actions → General** اسمح بالـ Actions.

---

## الطريقة 2 — بدون Git (تحميل ZIP)

1. نزّل:

   https://github.com/amrselimali/Universal-Business-Platform/archive/refs/heads/v2-standalone.zip

2. فك الضغط.
3. افتح الـ Repo الجديد → **Add file → Upload files**.
4. اسحب كل محتويات المجلد (وليس المجلد نفسه) وارفعها.

> GitHub يرفع المجلدات والملفات معاً، لكن تأكد أن `src/` و`tests/` في الجذر.

---

## بعد النقل — خطوات التشغيل

```bash
docker compose up -d postgres
dotnet run --project src/Ubp.Web
```

- الرابط: `http://localhost:5080`
- الدخول الأول: `admin / Admin@123` — **غيّرها فوراً**.

---

## تفعيل CI

ملف الـ workflow موجود في:

```
ci/ci.yml
```

انقله إلى:

```
.github/workflows/ci.yml
```

ثم ارفعه. يحتاج الـ GitHub App صلاحية **Workflows**، أو استخدم حسابك مباشرة من GitHub UI:
**Add file → Create new file →** `.github/workflows/ci.yml` ثم الصق المحتوى.

---

## التحقق قبل النقل

هذه النسخة تم اختبارها محلياً:

- `dotnet build` ✅
- `dotnet test` → **25/25** ✅
- QA suite → **9/9** ✅
- اختبار واجهة على سطح المكتب والموبايل ✅

---

## ملاحظة عن الصلاحيات

البيئة الآلية المستخدمة لإنشاء المشروع لا تملك صلاحية الكتابة على Repository جديد،
لذلك تم تجهيز النسخة المستقلة على فرع `v2-standalone` داخل المستودع الحالي.
بمجرد تنفيذ خطوات النقل أعلاه، يصبح المشروع مستقلاً تماماً في Repo خاص به.
