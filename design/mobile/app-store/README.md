# Fenpo for iPhone: App Store görselleri

App Store Connect'e yüklenen 6.9 inç görseller (1320 × 2868), dile ve temaya göre, yükleme sırasıyla numaralı:
1 Ana ekran, 2 Aktarımlar, 3 Web kamerası, 4 PC, 5 Fare ve klavye, 6 Sunum kumandası, 7 Ayarlar.

CI'da üretilir: Actions › iOS › Run workflow › **Only take the App Store screenshots**. Simülatör ekranlarını
`ios/scripts/screenshots.sh` çeker (örnek oturumun uydurma içeriğiyle), başlık ve çerçeveyi
`ios/scripts/store-frames.py` ekler. Metinler, bilgiler ve kurallar: `docs/ios/APP-STORE.md`.
