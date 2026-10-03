# Production handoff — 3 Oktober 2026

## Status

Belum boleh ditag atau dipublikasikan sebagai production. Aplikasi pada OPPO A58 dan PC pengujian sudah dikonfirmasi pengguna bekerja melalui USB, termasuk warna indikator. Bukti ini belum menggantikan instalasi pada Windows bersih dengan Secure Boot aktif dan TESTSIGNING mati.

## Perubahan yang diselesaikan

- USB reader Windows tidak lagi menahan attachment/state pump; tes native-shaped mencakup Wi-Fi → USB → Wi-Fi → Bluetooth.
- Installer Windows melewati pengujian instalasi, peluncuran nyata, upgrade, restart service dan uninstall.
- Warna indikator Android mengikuti transport authoritative.
- Build release Android selalu memerlukan signing; kunci/subject Android Debug ditolak.
- Preflight release menginventarisasi semua konfigurasi sekaligus tanpa mencetak rahasia.
- Changelog harus memiliki header final yang cocok persis; header `-dev` tidak memenuhi release stabil.
- CI menyiapkan payload driver unsigned untuk pengujian HLK/submission. Payload ini tidak boleh dipasang atau disebarkan sebagai driver production.

## Yang belum tersedia di konfigurasi GitHub

Audit input pada 3 Oktober menemukan konfigurasi berikut belum tersedia:

- Keystore Android production dan empat secret signing.
- Sertifikat Authenticode Windows dan dua secret signing serta timestamp URL.
- Bundle driver yang dikembalikan Microsoft dengan signature retail dan SHA-256.
- Persetujuan lisensi Inno Setup dan LICENSE produk yang dipilih pemilik.
- Persetujuan physical acceptance untuk SHA source yang tepat.

VERSION masih `0.1.1-dev`. Version/changelog final dan kenaikan VERSION_CODE dilakukan saat kandidat release sudah diterima.

## Urutan penyelesaian

1. Pemilik menetapkan lisensi produk serta penggunaan Inno Setup.
2. Sediakan identitas signing Android production dan sertifikat Windows dari CA; simpan sebagai secrets, jangan commit private key.
3. Lengkapi akun Hardware Dev Center dengan EV certificate, jalankan HLK pada perangkat fisik yang sesuai, lalu submit paket melalui jalur WHCP/HLK.
4. Setelah Microsoft mengembalikan driver, validasi signature, scope hardware, membership katalog dan hash; pasang URL/hash bundle pada variables repo.
5. Build kandidat, uji clean install, 30 menit dan 2 jam gameplay, sleep/resume, CPU/baterai, upgrade/uninstall dan akses XInput sebagai pengguna biasa.
6. Catat bukti melalui template production, pin persetujuan ke commit yang diuji, lalu jalankan release workflow.

USB Direct saat ini dibatasi scope bootstrap OPPO A58 / CPH2577 yang sudah divalidasi. Pergantian sertifikat dari APK debug ke production adalah migrasi awal tersendiri dan memerlukan pairing ulang; update production berikutnya mempertahankan sertifikat production.

## Referensi

- [Microsoft driver signing](https://learn.microsoft.com/windows-hardware/drivers/dashboard/driver-signing-offerings)
- [Microsoft certificate requirements](https://learn.microsoft.com/windows-hardware/drivers/dashboard/code-signing-reqs)
- [Runbook repo](windows-retail-driver-runbook.md)
- [Kontrak readiness](production-readiness.md)
