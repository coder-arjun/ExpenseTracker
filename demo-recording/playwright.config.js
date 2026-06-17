// Auto-recording config for the ExpenseTracker client demo.
// One worker, no retries, slow pacing, 1080p video — produces a single
// .webm under test-results/ that we then surface as the deliverable.
//
// Target = the running ASP.NET Core dev server at https://localhost:7277.
// HTTPS errors are ignored because the cert is the self-signed dev cert.
/** @type {import('@playwright/test').PlaywrightTestConfig} */
module.exports = {
  testDir: './tests',
  timeout: 5 * 60 * 1000,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'https://localhost:7277',
    viewport: { width: 1920, height: 1080 },
    headless: true,
    ignoreHTTPSErrors: true,
    video: { mode: 'on', size: { width: 1920, height: 1080 } },
    launchOptions: { slowMo: 600 },
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
};
