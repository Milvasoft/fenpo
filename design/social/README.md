# Fenpo sosyal medya paketi

10 video ve 10 görsel, Türkçe ve İngilizce. Hepsi bu klasördeki kaynaklardan yeniden üretilebilir; müzikler de kodla sentezlenir, dışarıdan ses ya da görsel kullanılmadı (Gabarito fontu hariç, OFL).

## Çıktılar

| Klasör | İçerik |
|---|---|
| `videos/tr/`, `videos/en/` | 8 Instagram reel (1080×1920, 30 fps) ve 2 LinkedIn videosu (1920×1080), H.264 + AAC, yaklaşık −13 LUFS. Repoya girmez (~470 MB); `src/build.py` ile yeniden üretilir |
| `posts/tr/`, `posts/en/` | 10 görsel, her biri iki biçimde: `-ig` Instagram 1080×1350 (4:5), `-li` LinkedIn 2400×1254 (1200×627'nin 2 katı, 1.91:1) |
| `index.html` | Hepsini tarayıcıda yan yana gösteren önizleme sayfası (Türkçe/English) |

### Videolar

| Dosya | Süre | Konu | Müzik |
|---|---|---|---|
| `r01-tanitim` | 20 sn | "Telefonuna uzanmayı bırak": telefon, bağlantı, 8 özellik vuruşta, ekran görüntüleri | House, 124 BPM, Fa minör |
| `r02-bildirimler` | 22 sn | Titreyen telefon, Windows bildirimi, bildirimden yanıt, Bildirimler sayfası | Marimba pop, 110 BPM, Si♭ majör |
| `r03-aramalar-mesajlar` | 23 sn | Ada'da gelen arama, klavyeyle mesajlaşma, Mesajlar ve Aramalar sayfaları | Lo-fi, 90 BPM, Do majör |
| `r04-pano` | 21 sn | Telefonda kopyala + tek dokunuş, Ctrl+V, ters yön, Pano sayfası | Minimal tech, 128 BPM, La minör |
| `r05-dosyalar` | 20 sn | Kablo/e-posta/bulut yok, paylaşım menüsü, sağ tık "Fenpo ile telefona gönder", Dosyalar sayfası | Future bass, 140 BPM yarım tempo, Re majör |
| `r06-kamera` | 21 sn | Kötü dizüstü kamerası, telefon monitörün üstüne, görüntülü görüşme, Kamera sayfası | Synthwave, 100 BPM, La minör |
| `r07-gizlilik` | 22 sn | Veriler buluta değil cihazlar arasında; bulut/hesap/reklam yok; TLS 1.3; gerçek kod | Sinematik, 84 BPM, Re minör → Fa majör |
| `r08-super-gucler` | 23 sn | Telefonu çaldır, yakınlık kilidi, hotspot, medya tuşları | Disco house, 118 BPM, Re dorian |
| `l01-tanitim` | 49 sn | Sorun → logo → 6 özellik turu (gerçek ekran görüntüleri) → değerler | Piyano ve yaylılar, 104 BPM, Sol majör |
| `l02-nasil-calisir` | 42 sn | Dört bağlantı yolu, QR eşleşme ve güvenlik, tek protokol üç uygulama, açık kaynak | Ambient elektronika, 92 BPM, Mi minör |

Ses efektleri (bildirim sesi, tıklama, whoosh, kilit) animasyondaki olaylardan otomatik yerleşir ve parçanın tonuna göre akort edilir.

### Görseller

1 Tanıtım · 2 Bildirimler · 3 Mesajlar ve aramalar · 4 Pano · 5 Dosyalar · 6 Kamera · 7 Gizlilik · 8 Nasıl çalışır · 9 iPhone ve Android · 10 Hepsi bir arada

## Yeniden üretmek

Gerekenler: Python 3 (numpy, scipy, Pillow), Node.js, ffmpeg (h264_nvenc ve libx264), Playwright'ın Chromium'u.

```bash
cd design/social/src
npm init -y && npm i playwright@1.56   # bir kez, istediğin bir klasörde
export PLAYWRIGHT_DIR=<playwright'ın kurulu olduğu klasör>
python build.py --lang tr,en            # bütün videolar
python build.py r04-pano --lang tr      # tek video
python build.py --lang tr --audio-only  # kareleri yeniden çizmeden müziği ve birleştirmeyi yenile
node render.mjs still "posts/posts.html?post=3&fmt=ig" tr ../posts/tr/fenpo-post-03-ig-tr.png 0
```

| Kaynak | Ne yapar |
|---|---|
| `src/lib/engine.js` | Zamana bağlı animasyon motoru: her kare `seek(t)` ile belirlenir, render kare kare ekran görüntüsü alır |
| `src/lib/components.js`, `core.css` | Logo, telefon, Windows bildirimi, Ada, imleç, kapanış kartı, arka plan; renkler tasarım sözlüğünden |
| `src/videos/*.html` | Her video bir sayfa; tarayıcıda açınca kendini oynatır (`?lang=en`, `?t=5&still`) |
| `src/posts/posts.html` | 10 görsel, `?post=1..10&fmt=ig|li` |
| `src/audio/synth.py`, `music.py` | Sentezleyici (davul, bas, pad, FM piyano, zil, efektler, reverb, sidechain) ve her videonun bestesi |
| `src/render.mjs`, `build.py` | Kare yakalama → ffmpeg; müzik; birleştirme ve ses seviyesi |

Ara dosyalar `.work/` altında; silinebilir.

## Paylaşım metni önerileri

**Instagram (TR)**
- r01: Telefonuna uzanmayı bırak. Bildirimler, mesajlar, aramalar, pano, dosyalar ve kamera; hepsi Windows'ta. Bulut yok, hesap yok. #Fenpo #Windows #iPhone #Android #verimlilik
- r02: Telefon titredi, bakmana gerek yok. Bildirim Windows'a düşer, oradan yanıtla.
- r03: Telefon çantada çalıyor mu? Bilgisayardan yanıtla. Mesajlarını da klavyeyle yaz.
- r04: Telefonda kopyala, bilgisayarda yapıştır. Tersi de çalışır.
- r05: Kablo yok, kendine e-posta yok. Sağ tıkla, telefona gönder.
- r06: Dizüstü kameran hâlâ bulanık mı? Telefonun artık web kameran.
- r07: Verilerin hiçbir yere gitmez. Bulut yok, hesap yok, reklam yok. Açık kaynak.
- r08: Fenpo'nun küçük süper güçleri: telefonu çaldır, uzaklaşınca kilitle, hotspot'a tek tıkla bağlan, medya tuşlarıyla yönet.

**LinkedIn (TR)**
- l01: Telefonla bilgisayar aynı masada ama ayrı dünyalarda. Fenpo, iPhone ve Android telefonları Windows'la tek cihaz gibi çalıştıran açık kaynak bir uygulama: bildirimler, mesajlar, aramalar, pano, dosyalar ve kamera. Sunucu yok, hesap yok; her şey cihazlar arasında, şifreli. github.com/Milvasoft/fenpo
- l02: Sunucusuz bir telefon–Windows köprüsü nasıl çalışır? Dört bağlantı yolu (BLE GATT, klasik Bluetooth, Fenpo BLE kanalı, Wi‑Fi üzerinde TLS 1.3), QR ile eşleşme, karşılıklı sertifika sabitleme ve üç uygulamanın paylaştığı tek bir Protobuf sözleşmesi. Kodun hepsi açık.

İngilizce metinler videolardaki İngilizce cümlelerle aynı çizgide yazılabilir.

## Notlar

- Arayüzde ve metinlerde Apple ve Microsoft özellik adları geçmez (CLAUDE.md). "iPhone", "Android", "Windows" yalnızca uyumluluğu anlatmak için kullanıldı.
- Ekran görüntüleri `design/screenshots/windows/` altındaki gösterim verili görüntüler; gerçek kişisel veri yok.
- Aramalar bölümü uygulamanın bugünkü davranışını anlatır: arama bilgisayardan yanıtlanır ya da reddedilir, görüşme telefonda sürer.
