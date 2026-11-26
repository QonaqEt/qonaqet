<?php
// QONAQET webhook imzasının yoxlanması — PHP (7.4+). Özünü yoxlama: `php webhook-verify.php`.

/**
 * @param string $rawBody Xam body: file_get_contents('php://input')
 */
function qonaqet_verify_webhook(string $secret, string $timestamp, string $rawBody, string $signatureHeader, ?int $now = null): bool
{
    $now = $now ?? time();
    if (!ctype_digit($timestamp) || abs($now - (int) $timestamp) > 300) { // 5 dəqiqə
        return false;
    }

    $expected = 'sha256=' . hash_hmac('sha256', $timestamp . '.' . $rawBody, $secret);
    return hash_equals($expected, $signatureHeader);
}

if (realpath($_SERVER['SCRIPT_FILENAME'] ?? '') === __FILE__) {
    $sig = 'sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789';
    $ok = qonaqet_verify_webhook('whsec_test', '1700000000', '{"a":1}', $sig, 1700000060);
    $stale = qonaqet_verify_webhook('whsec_test', '1700000000', '{"a":1}', $sig, 1700000301);
    echo ($ok && !$stale) ? "ok\n" : "FAIL\n";
    exit(($ok && !$stale) ? 0 : 1);
}
