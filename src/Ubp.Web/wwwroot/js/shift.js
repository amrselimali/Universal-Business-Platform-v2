/* طباعة الشفت: طباعة تلقائية بعد الرسم (يمكن الحفظ كـ PDF) */
window.ubpPrintOnLoad = function () {
    setTimeout(function () { window.print(); }, 450);
};

/* فتح صفحة تصدير الشفت في تبويب جديد */
window.ubpOpenPrint = function (url) {
    if (url) window.open(url, '_blank', 'noopener');
};
