// QONAQET webhook imzasının yoxlanması — Node.js (18+). Özünü yoxlama: `node webhook-verify.js`.
const crypto = require('node:crypto');

/**
 * @param {string} secret           Abunə yaradılanda verilən whsec_… secret
 * @param {string} timestamp        X-Qonaqet-Timestamp başlığı
 * @param {string} rawBody          Xam body (JSON.parse/stringify etmədən)
 * @param {string} signatureHeader  X-Qonaqet-Signature başlığı
 */
function verifyQonaqetWebhook(secret, timestamp, rawBody, signatureHeader, nowSeconds = Math.floor(Date.now() / 1000)) {
  const ts = Number(timestamp);
  if (!Number.isInteger(ts) || Math.abs(nowSeconds - ts) > 300) return false; // 5 dəqiqə
  const expected = Buffer.from('sha256=' + crypto.createHmac('sha256', secret).update(`${timestamp}.${rawBody}`).digest('hex'));
  const actual = Buffer.from(signatureHeader || '');
  return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
}

module.exports = { verifyQonaqetWebhook };

if (require.main === module) {
  const sig = 'sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789';
  const ok = verifyQonaqetWebhook('whsec_test', '1700000000', '{"a":1}', sig, 1700000060);
  const stale = verifyQonaqetWebhook('whsec_test', '1700000000', '{"a":1}', sig, 1700000301);
  console.log(ok && !stale ? 'ok' : 'FAIL');
  process.exit(ok && !stale ? 0 : 1);
}
