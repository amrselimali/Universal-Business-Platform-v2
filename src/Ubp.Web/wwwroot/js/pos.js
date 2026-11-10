window.ubpPrint = function () {
    const receipt = document.querySelector('.ubp-print-receipt');
    if (receipt) document.body.classList.add('ubp-pos-print');
    window.print();
    if (receipt) {
        window.addEventListener('afterprint', function handler() {
            document.body.classList.remove('ubp-pos-print');
            window.removeEventListener('afterprint', handler);
        });
    }
};
