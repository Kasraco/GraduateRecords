/**
 * Administration form behavior (vanilla ES module, DESIGN §14).
 *
 * 1. Double-submit protection: the submit button is disabled as soon as a form
 *    submits. This is UX only — the server never relies on it (AGENTS §11).
 * 2. Confirmation for destructive actions: a form carrying `data-confirm` asks
 *    for confirmation before it submits. The message is authored by the server
 *    (Razor-encoded) and is only ever read as text — it is never inserted into
 *    the DOM as markup (AGENTS §18).
 *
 * Never uses innerHTML/outerHTML/insertAdjacentHTML.
 */

document.addEventListener(
  "submit",
  (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) {
      return;
    }

    const message = form.getAttribute("data-confirm");
    if (message && !window.confirm(message)) {
      event.preventDefault();
      return;
    }

    for (const button of form.querySelectorAll('button[type="submit"]')) {
      button.disabled = true;
      button.setAttribute("aria-disabled", "true");
    }
  },
  true,
);
