/* طباعة الشفت: طباعة تلقائية بعد الرسم (يمكن الحفظ كـ PDF) */
window.ubpPrintOnLoad = function () {
    setTimeout(function () { window.print(); }, 450);
};
