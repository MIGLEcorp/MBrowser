const { chromium } = require(require("path").join(__dirname, "..", "dist", "Features", "WebKit", ".playwright", "package"));
(async () => {
  const browser = await chromium.launch({
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
    headless: true,
    args: ["--no-first-run", "--no-default-browser-check", "--disable-extensions"]
  });
  for (const url of ["https://accounts.google.com/", "https://github.com/", "https://funpay.com/"]) {
    const page = await browser.newPage();
    const start = Date.now();
    try {
      const res = await page.goto(url, {waitUntil:"domcontentloaded", timeout:35000});
      console.log("SITE " + url + " HTTP " + res?.status() + " DOM_WALL_MS " + (Date.now() - start));
      try {await page.waitForLoadState("load", {timeout:15000})}
      catch (e) {console.log("LOAD_WAIT " + e.message.substring(0,130))}
      const timing = await page.evaluate(() => {
        const n = performance.getEntriesByType("navigation")[0];
        return {host: location.host, title: document.title,
          ttfb: Math.round(n?.responseStart ?? -1),
          dom: Math.round(n?.domContentLoadedEventEnd ?? -1),
          load: Math.round(n?.loadEventEnd ?? -1),
          resourceCount: performance.getEntriesByType("resource").length};
      });
      console.log("METRICS " + JSON.stringify(timing) + " WALL_MS " + (Date.now()-start));
    } catch(e) {
      console.log("SITE_ERROR " + url + " " + e.message.substring(0,270) + " WALL_MS " + (Date.now() - start));
    } finally {await page.close();}
  }
  await browser.close();
})().catch(e=>{console.error(e);process.exitCode=1;});