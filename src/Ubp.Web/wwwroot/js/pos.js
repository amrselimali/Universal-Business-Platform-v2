window.ubpPrint = function () {
    const receipt = document.getElementById('pos-print-area');
    if (receipt) document.body.classList.add('ubp-pos-print');
    window.print();
    if (receipt) {
        window.addEventListener('afterprint', function handler() {
            document.body.classList.remove('ubp-pos-print');
            window.removeEventListener('afterprint', handler);
        });
    }
};
