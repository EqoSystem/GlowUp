// api/email-otp.js: the code, the Gmail dialogue (fake socket), the answers when it is not set up or the sign-in is wrong. Nothing leaves the machine.
import { createRequire } from "node:module";
import { EventEmitter } from "node:events";
const mod = createRequire(import.meta.url)("../api/email-otp.js");
let pass = 0, fail = 0;
const check = (n, ok) => { if (ok) pass++; else { fail++; console.log("FAIL", n); } };

const secret = "x".repeat(40);
const now = Date.UTC(2026, 10, 22, 10, 0, 0);
const code = mod.codeFor(secret, "uid1", "a@b.com", Math.floor(now / 600000));
check("code has 6 digits", /^\d{6}$/.test(code));
check("same account, same window: same code", code === mod.codeFor(secret, "uid1", "a@b.com", Math.floor(now / 600000)));
check("other account: other code (almost surely)", code !== mod.codeFor(secret, "uid2", "a@b.com", Math.floor(now / 600000)));
check("right code accepted", mod.codeIsRight(secret, "uid1", "a@b.com", code, now));
check("code with spaces around accepted", mod.codeIsRight(secret, "uid1", "a@b.com", " " + code + " ", now));
check("code still good in the next window", mod.codeIsRight(secret, "uid1", "a@b.com", code, now + 600000));
check("code dead two windows later", !mod.codeIsRight(secret, "uid1", "a@b.com", code, now + 1300000));
check("wrong code refused", !mod.codeIsRight(secret, "uid1", "a@b.com", code === "000000" ? "000001" : "000000", now));
check("letters refused", !mod.codeIsRight(secret, "uid1", "a@b.com", "12345a", now));
check("short code refused", !mod.codeIsRight(secret, "uid1", "a@b.com", "12345", now));
check("another account's code refused", !mod.codeIsRight(secret, "uid2", "a@b.com", code, now));
check("the code is bound to the e-mail address", !mod.codeIsRight(secret, "uid1", "other@b.com", code, now));
check("the e-mail is compared without case", mod.codeIsRight(secret, "uid1", "A@B.com", code, now));

// a scripted Gmail server on a fake socket
function fakeServer(refuseAuth) {
    const written = [];
    const socket = new EventEmitter();
    socket.end = () => socket.emit("close");
    const replies = refuseAuth ? ["220 ready", "250 ok", "535 bad credentials"] : ["220 ready", "250-x\r\n250 ok", "334 u", "334 p", "235 ok", "250 ok", "250 ok", "354 go", "250 queued", "221 bye"];
    let i = 0;
    const say = () => { if (i < replies.length) { const r = replies[i++]; setImmediate(() => socket.emit("data", Buffer.from(r + "\r\n"))); } };
    socket.write = (s) => { written.push(String(s)); say(); };
    setImmediate(say);
    return { socket, written };
}
{
    const s = fakeServer(false);
    await mod.smtpSend({ user: "me@gmail.com", pass: "app-pass", to: "a@b.com", subject: "S\r\nBcc: evil@x.com", text: "Code 123456\r\n.\r\nline" }, () => s.socket);
    const all = s.written.join("");
    check("mail dialogue completes", all.includes("QUIT"));
    check("recipient sent", all.includes("RCPT TO:<a@b.com>"));
    check("subject cannot add a header", !/\r\nBcc:/.test(all));
    check("a lone dot line is escaped", all.includes("\r\n..\r\n"));
    check("password sent only as base64", !all.includes("app-pass") && all.includes(Buffer.from("app-pass").toString("base64")));
}
{
    const s = fakeServer(true);
    let failed = false;
    try { await mod.smtpSend({ user: "me@gmail.com", pass: "bad", to: "a@b.com", subject: "S", text: "T" }, () => s.socket); } catch (e) { failed = String(e.message).includes("535"); }
    check("refused login is an error", failed);
}

// the handler
const call = (method, body) => new Promise((resolve) => {
    const res = { setHeader() {}, end(t) { resolve({ status: this.statusCode, body: JSON.parse(t) }); } };
    mod({ method, headers: {}, body }, res);
});
delete process.env.GLOWUP_OTP_SECRET;
check("not set up: 503", (await call("POST", { action: "send" })).status === 503);
check("GET refused", (await call("GET")).status === 405);
process.env.GLOWUP_OTP_SECRET = secret; process.env.GMAIL_USER = "me@gmail.com"; process.env.GMAIL_APP_PASSWORD = "pw";
process.env.FIREBASE_SERVICE_ACCOUNT = JSON.stringify({ client_email: "x@y.iam", private_key: "k" });
globalThis.fetch = async () => ({ ok: false, status: 400, json: async () => ({}) });
check("bad sign-in: 401", (await call("POST", { action: "send", idToken: "t" })).status === 401);
// the handler with a fake Google: lookup, token, atomic counters, update, mail
{
    const { generateKeyPairSync } = await import("node:crypto");
    const { privateKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
    process.env.FIREBASE_SERVICE_ACCOUNT = JSON.stringify({ client_email: "x@y.iam", private_key: privateKey.export({ type: "pkcs8", format: "pem" }) });
    const counters = new Map(); let updated = 0, commitsFail = false, user = { localId: "uidX", email: "Me@Mail.com", emailVerified: false };
    globalThis.fetch = async (url, o) => {
        const u = String(url); const json = (b, st = 200) => ({ ok: st < 400, status: st, json: async () => b });
        if (u.includes("accounts:lookup")) return json({ users: [user] });
        if (u.includes("oauth2.googleapis.com")) return json({ access_token: "tok" });
        if (u.includes(":commit")) {
            if (commitsFail) return json({}, 500);
            const name = JSON.parse(o.body).writes[0].transform.document; counters.set(name, (counters.get(name) || 0) + 1);
            return json({ writeResults: [{ transformResults: [{ integerValue: String(counters.get(name)) }] }] });
        }
        if (u.includes("accounts:update")) { updated++; return json({}); }
        return json({}, 404);
    };
    const mailed = [];
    // the handler sends mail through smtpSend over a real TLS socket: replace the socket by the fake server for these calls
    const tlsMod = createRequire(import.meta.url)("node:tls");
    const realConnect = tlsMod.connect;
    tlsMod.connect = () => { const s = fakeServer(false); const w = s.written; mailed.push(w); return s.socket; };
    const good = (action, extra = {}) => call("POST", { action, idToken: "t", ...extra });
    const r1 = await good("send");
    check("send: 200 and one mail", r1.status === 200 && mailed.length === 1);
    const sentCode = (mailed[0].join("").match(/Subject: Your GlowUp code: (\d{6})/) || [])[1];
    check("the mail carries a 6-digit code", /^\d{6}$/.test(sentCode || ""));
    check("send again at once: asked to wait", (await good("send")).status === 429);
    commitsFail = true;
    check("counter write fails: send refused (fail closed)", (await good("send")).status === 502);
    check("counter write fails: verify refused", (await good("verify", { code: sentCode })).status === 502);
    commitsFail = false;
    for (let i = 0; i < 5; i++) check("wrong code " + (i + 1) + " is only 'no'", (await good("verify", { code: sentCode === "111111" ? "222222" : "111111" })).body.ok === false);
    check("the 6th guess is locked, even with the right code", (await good("verify", { code: sentCode })).status === 429);
    check("nothing was confirmed by wrong or locked tries", updated === 0);
    counters.clear();
    const ok = await good("verify", { code: sentCode });
    check("right code confirms the e-mail", ok.status === 200 && ok.body.ok === true && updated === 1);
    user = { localId: "uidX", email: "new@mail.com", emailVerified: false };
    counters.clear();
    check("a code made for the old e-mail does not work", (await good("verify", { code: sentCode })).body.ok === false);
    user = { localId: "uidX", email: "me@mail.com", emailVerified: true };
    check("an already confirmed account is told so", (await good("verify", { code: "000000" })).body.already === true);
    tlsMod.connect = realConnect;
}
console.log(fail === 0 ? `ALL PASSED  (checks made: ${pass})` : `${fail} FAILED  (checks made: ${pass + fail})`);
process.exit(fail ? 1 : 0);
