// Botão mostrar/ocultar senha. Sem script inline (CSP). Uso:
// <button type="button" data-toggle-password="#IdDoCampo" ...>
(function () {
    // Emojis de cada estado (troque aqui se preferir outros).
    var HIDDEN = '\u{1F648}';                            // 🙈 senha oculta (olhos fechados)
    var VISIBLE = '\u{1F435}';                            // 🐵 senha visível (macaquinho de olhos abertos)
    var STETHOSCOPE = '\u{1FA7A}';                        // 🩺

    document.querySelectorAll('[data-toggle-password]').forEach(function (button) {
        var selector = button.getAttribute('data-toggle-password');
        if (!selector) return;
        var input = document.querySelector(selector);
        if (!input) return;

        function render(visible) {
            input.type = visible ? 'text' : 'password';
            button.classList.toggle('is-visible', visible);
            if (!button.querySelector('.password-eye')) {
                button.textContent = STETHOSCOPE + (visible ? VISIBLE : HIDDEN);
            }
            button.setAttribute('aria-pressed', visible ? 'true' : 'false');
            button.setAttribute('aria-label', visible ? 'Ocultar senha' : 'Mostrar senha');
            button.title = visible ? 'Ocultar senha' : 'Mostrar senha';
        }

        button.addEventListener('click', function () {
            render(input.type === 'password');
            input.focus();
        });
        render(false);
    });
})();
