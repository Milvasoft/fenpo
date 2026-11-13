# Fenpo sosyal medya paketi

10 video ve 10 görsel, Türkçe ve İngilizce. Hepsi bu klasördeki kaynaklardan yeniden üretilebilir. Müzikler Pixabay'den telifsiz parçalar; görseller ve animasyonlar bu klasörde çiziliyor (Gabarito fontu OFL).

## Çıktılar

| Klasör | İçerik |
|---|---|
| `videos/tr/`, `videos/en/` | 8 Instagram reel (1080×1920, 30 fps) ve 2 LinkedIn videosu (1920×1080), H.264 + AAC, yaklaşık −13 LUFS. Repoya girmez (~470 MB); `src/build.py` ile yeniden üretilir |
| `posts/tr/`, `posts/en/` | 10 görsel, her biri iki biçimde: `-ig` Instagram 1080×1350 (4:5), `-li` LinkedIn 2400×1254 (1200×627'nin 2 katı, 1.91:1) |
| `index.html` | Hepsini tarayıcıda yan yana gösteren önizleme sayfası (Türkçe/English) |

### Videolar

| Dosya | Konu | Müzik (Pixabay) |
|---|---|---|
| `r01-tanitim` | "Telefonuna uzanmayı bırak": telefon, bağlantı, 8 özellik vuruşta, ekran görüntüleri | [Better Machine Future House](https://pixabay.com/music/upbeat-better-machine-future-house-128bpm-141511/), CONATHOR, 128 BPM |
| `r02-bildirimler` | Titreyen telefon, Windows bildirimi, bildirimden yanıt, Bildirimler sayfası | [Soft Positive Summer Pop](https://pixabay.com/music/pop-soft-positive-summer-pop-218419/), Emmraan, 125 BPM |
| `r03-aramalar-mesajlar` | Ada'da gelen arama, klavyeyle mesajlaşma, Mesajlar ve Aramalar sayfaları | [Lofi Hip Hop](https://pixabay.com/music/beats-lofi-hip-hop-background-lofi-music-270003/), SigmaMusicArt, 80 BPM |
| `r04-pano` | Telefonda kopyala + tek dokunuş, Ctrl+V, ters yön, Pano sayfası | [Ardor](https://pixabay.com/music/upbeat-ardor-future-house-music-170864/), kontraa, 128 BPM |
| `r05-dosyalar` | Kablo/e-posta/bulut yok, paylaşım menüsü, sağ tık "Fenpo ile telefona gönder", Dosyalar sayfası | [Energetic Upbeat Future Bass](https://pixabay.com/music/future-bass-energetic-upbeat-future-bass-514142/), BombinSound, 140 BPM |
| `r06-kamera` | Kötü dizüstü kamerası, telefon monitörün üstüne, görüntülü görüşme, Kamera sayfası | [Neon Dreams](https://pixabay.com/music/synthwave-neon-dreams-90s-synthwave-344049/), lofidreams, 100 BPM |
| `r07-gizlilik` | Veriler buluta değil cihazlar arasında; bulut/hesap/reklam yok; TLS 1.3; gerçek kod | [Cinematic Tension](https://pixabay.com/music/action-cinematic-tension-620587/), Kulakovka, 100 BPM |
| `r08-super-gucler` | Telefonu çaldır, yakınlık kilidi, hotspot, medya tuşları | [70s Funky Disco](https://pixabay.com/music/funk-70s-funky-disco-119bpm-510309/), pietiX, 119 BPM |
| `l01-tanitim` | Sorun → logo → 6 özellik turu (gerçek ekran görüntüleri) → değerler | [Inspiring Corporate Background](https://pixabay.com/music/upbeat-inspiring-corporate-background-344952/), Music_for_Creators, 120 BPM |
| `l02-nasil-calisir` | Dört bağlantı yolu, QR eşleşme ve güvenlik, tek protokol üç uygulama, açık kaynak | [Digital Technology](https://pixabay.com/music/corporate-digital-technology-617560/), AtlasAudio, 100 BPM |

Müzikler Pixabay'den, [Pixabay Content License](https://pixabay.com/service/license-summary/) ile: ticari kullanım ve sosyal medyada paylaşım serbest, kaynak göstermek gerekmez (yine de iyi olur). Hepsi "Content ID Registered" işareti olmayan parçalardan seçildi; işaretli parçalar Instagram ve YouTube'da telif uyarısı alabiliyor. Lisans, müziğin tek başına yeniden dağıtılmasına izin vermediği için `src/audio/tracks/*.mp3` repoya girmez; `tracks.json`'daki bağlantılardan yeniden indirilir.

Her video parçanın temposunda render edilir (`?bpm=`), yani sahne geçişleri parçanın vuruşlarına oturur. `src/audio/track.py` parçada videonun "drop" ölçüsüne en sert yükselişin denk geldiği vuruşu bulur, sessiz boşluk içeren kesitleri eler, parçayı video boyunda keser ve kapanış kartında söndürür. Ses efektleri (bildirim sesi, tıklama, whoosh, kilit) animasyondaki olaylardan otomatik yerleşir ve müziğin altında kalır.

İlk sürümdeki sentezlenmiş besteler duruyor: `python build.py --synth` onları videonun özgün temposuyla kullanır.

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
# parça değiştirmek: mp3'ü src/audio/tracks/ altına koy, sonra
python audio/settrack.py r04-pano <dosya.mp3> <pixabay sayfası> "<ad>" "<sanatçı>" <sanatçı sayfası> 0.4,0.6,1,1,1
node render.mjs still "posts/posts.html?post=3&fmt=ig" tr ../posts/tr/fenpo-post-03-ig-tr.png 0
```

| Kaynak | Ne yapar |
|---|---|
| `src/lib/engine.js` | Zamana bağlı animasyon motoru: her kare `seek(t)` ile belirlenir, render kare kare ekran görüntüsü alır |
| `src/lib/components.js`, `core.css` | Logo, telefon, Windows bildirimi, Ada, imleç, kapanış kartı, arka plan; renkler tasarım sözlüğünden |
| `src/videos/*.html` | Her video bir sayfa; tarayıcıda açınca kendini oynatır (`?lang=en`, `?t=5&still`) |
| `src/posts/posts.html` | 10 görsel, `?post=1..10&fmt=ig|li` |
| `src/audio/tracks.json`, `track.py` | Her videonun Pixabay parçası (kaynak, sanatçı, lisans, tempo analizi) ve parçayı kesip efektlerle karıştıran adım |
| `src/audio/beats.py`, `settrack.py` | Tempo, vuruş ve ölçü analizi; yeni bir parçayı bir videoya bağlama |
| `src/audio/synth.py`, `music.py` | Sentezleyici, ses efektleri ve ilk sürümün besteleri (`--synth`) |
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

- Arayüzde ve metinlerde Apple ve Microsoft özellik adları geçmez. "iPhone", "Android", "Windows" yalnızca uyumluluğu anlatmak için kullanıldı.
- Ekran görüntüleri `design/screenshots/windows/` altındaki gösterim verili görüntüler; gerçek kişisel veri yok.
- Aramalar bölümü uygulamanın bugünkü davranışını anlatır: arama bilgisayardan yanıtlanır ya da reddedilir, görüşme telefonda sürer.
