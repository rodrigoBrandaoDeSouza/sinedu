// Aplica o tema antes do primeiro paint (evita flash). Arquivo externo para permitir CSP sem 'unsafe-inline'.
try {
  var p = localStorage.getItem("sinedu.theme");
  var dark = p === "dark" || ((!p || p === "system") && matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.dataset.theme = dark ? "dark" : "light";
} catch (e) {
  document.documentElement.dataset.theme = matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}
