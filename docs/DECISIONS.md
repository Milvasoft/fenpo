# Kararlar

Alınmış kararların tek listesi. Bir karar değişirse eskisinin üstü çizilmez, yeni bir madde eklenir ve eskisine "yerini aldı: #n" yazılır. Tarihler 2026.

## Ürün

| # | Tarih | Karar | Neden |
|---|---|---|---|
| 1 | 15 Ekim | Ad **Fenpo** (fenestra + pomum). Alan adı veya paket adı çakışırsa yedek **FenpoLink** | Özgün, kısa, okunuşu tek türlü |
| 2 | 15 Ekim | Logo **Çekirdek** (`design/logo/svg/1-cekirdek.svg`). Diğer konseptler kayıt için duruyor | 16 px'te en iyi okunan işaret, Apple ve Microsoft logolarından uzak |
| 3 | 15 Ekim | Renk sistemi: Mürekkep `#1B1F3B`, Çiçek `#E8577E`, Yaprak `#2F8F6B`, Alacakaranlık `#4E54C8`, Cam `#DCE6F5`, Pervaz `#F6F7FB` | `design/logo/logos.html` |
| 4 | 15 Ekim | Üç uygulamada da **tema** (Sistem, Açık, Koyu) ve **dil** (Sistem dili, Türkçe, English) seçimi | İlk günden; sonradan eklemek her ekranı elden geçirmek demek |
| 5 | 15 Ekim | Sunucu ve hesap yok. Her şey Bluetooth ve yerel ağ üzerinden, uçtan uca şifreli | Gizlilik, maliyet, basitlik |
| 6 | 15 Ekim | Apple'ın özel protokolleri (gerçek Handoff, Universal Clipboard, iCloud) taklit edilmez | Kırılgan, App Store ve hukuk riski |
| 7 | 15 Ekim | Lisans MIT. GPL bileşenler (UxPlay) sadece ayrı çalıştırılabilir olarak | Açık kaynak, ticari kullanıma açık |

## Platformlar

| # | Tarih | Karar | Neden |
|---|---|---|---|
| 8 | 15 Ekim | **Windows:** .NET 10 LTS, WinUI 3 (Windows App SDK 2.5), MSIX, Windows 11 22H2+ | `docs/TECH-STACK.md` |
| 9 | 15 Ekim | **iPhone:** Swift 6.4, SwiftUI, iOS 18+, XcodeGen | Widget, Denetim Merkezi ve uzantılar sadece Swift ile; AccessorySetupKit iOS 18 |
| 10 | 15 Ekim | **Android de yapılacak**, iPhone ile paralel ayrı bir iş kolu olarak. Yerel **Kotlin + Jetpack Compose + Material 3**. Ayrıntılı teknoloji seçimleri Android oturumunun ilk aşamasında araştırılıp `docs/ANDROID.md`'ye yazılır | Expo/React Native elendi: işin çoğu (arka plan Bluetooth, servisler, sistem entegrasyonları) yine yerel modül olarak yazılacaktı; üç dil, iki dilden pahalı |
| 11 | 15 Ekim | Üç uygulama tek protokol konuşur: `protocol/fenpo/v1/*.proto`. İlk mesaj `Hello` ile platform ve yetenek pazarlığı | Windows tarafında platforma özel dal açmadan yeni istemci eklenebilsin |
| 12 | 15 Ekim | TLS sertifikaları ECDSA P-256 | Schannel, Network.framework ve Android'de sorunsuz |

## Geliştirme ortamı ve test

| # | Tarih | Karar | Neden |
|---|---|---|---|
| 13 | 15 Ekim | **Mac yok.** iOS kodu Windows'ta yazılır; derleme ve birim testleri **GitHub Actions macOS runner'ında** koşar. Cihaz testleri ortağın Mac'i ve bir iPhone ile yapılır | Mac almadan ilerlemek |
| 14 | 15 Ekim | iOS cihaz testi geliştirme bitince değil, **kilometre taşlarında** yapılır: (1) S4 ve S6 spike'ları, (2) eşleşme + dosya aktarımı, (3) pano ve paylaşım menüsü | En büyük iOS riskleri (arka plan Bluetooth, AccessorySetupKit) cihaz görmeden doğrulanamaz; geç fark edilirse mimari değişir |
| 15 | 15 Ekim | Gerçek uçtan uca test ortamı Buğra'nın PC'si ve iPhone'u. Ortak Mac'te derleyip **TestFlight** ile gönderir. Bunun için Apple Developer Program (yıllık 99$) gerekli | Windows ↔ iPhone akışı ancak ikisi bir aradayken test edilebilir |
| 16 | 15 Ekim | Android tamamen Windows'ta geliştirilir ve test edilir (Android Studio). Bluetooth için **gerçek bir Android telefon** gerekli, emülatör yetmez | |
| 17 | 15 Ekim | Repo GitHub'da. iOS CI için macOS runner gerekiyor: açık repoda ücretsiz, özel repoda dakikalar 10 kat sayılıyor | Açık kaynak planıyla uyumlu |

## Çalışma düzeni

| # | Tarih | Karar | Neden |
|---|---|---|---|
| 18 | 15 Ekim | **Üç ayrı Claude Code oturumu**, üç iş kolu: Windows (ana, protokolün sahibi), iPhone, Android | Her biri kendi klasöründe, birbirinin işini ezmeden |
| 19 | 15 Ekim | Git düzeni: Windows oturumu `fenpo` klasöründe `main` dalında. iPhone `..\fenpo-ios` (`ios` dalı), Android `..\fenpo-android` (`android` dalı) worktree'lerinde. Mobil oturumlar her iş bloğunun başında `git merge main` yapar; Buğra kilometre taşlarında dalları `main`'e birleştirir | Aynı klasörde üç oturum birbirinin git index'ine karışır |
| 20 | 15 Ekim | Protokol değişikliği her zaman ayrı bir commit (`protocol: ...`), `protocol/` ve `docs/ARCHITECTURE.md` birlikte. Mobil oturumlar değişiklik gerekirse kendi dallarında yapar ve Buğra'ya söyler; Buğra hemen `main`'e alır | Üç uygulamanın sözleşmesi tek yerde, sapmadan |
| 21 | 15 Ekim | Başlatma promptları `docs/KICKOFF.md` içinde | |
| 22 | 15 Ekim | Windows spike sırası: S1 (S8 ve RSSI dahil), S2, S3, S4'ün Windows tarafı, S5'in Windows tarafı, S7 | Yakınlık kilidi Aşama 1'de ama RSSI kaynağı belirsiz; iOS kilometre taşı 1 (S4) PC'de bir çevre birim istiyor |
| 23 | 15 Ekim | Protokol v1 boşlukları S5 sırasında tek bir `protocol: ...` commit'iyle kapatılır: 6 haneli yedek kodun güvenliği, eşleşme bağlantısının `PairingResult` sonrası akışı, `PairingRequest.certificate_sha256` (PC parmak izini TLS'ten hesaplar), BLE kanalında AES-GCM nonce düzeni ve şifreleme/parçalama sırası, veri bağlantısında `TransferStreamHeader` çerçevesi | Taslakta tanımsız veya güvensiz kalan yerler |
| 24 | 15 Ekim | Cihazdan kaydedilen ham baytlar `captures/` altında kalır ve repoya girmez; repoya sadece metinleri ve numaraları değiştirilmiş fixture'lar girer | Kayıtlar gerçek mesaj ve kişi içeriği taşıyor |
| 25 | 15 Ekim | Repo **açık**, Milvasoft hesabı altında: `https://github.com/Milvasoft/fenpo` | iOS CI'ın macOS dakikaları açık repoda ücretsiz (#17) |
| 26 | 15 Ekim | Apple Developer Program hesabı Milvasoft'un mevcut hesabı | Hesap zaten var |
| 27 | 15 Ekim | Android test telefonu var; Android oturumu cihaza USB üzerinden `adb` ile bağlanır | Bluetooth için gerçek cihaz şart (#16) |
| 28 | 16 Ekim | Aşama 1 eşleşmesi Windows Ayarlar üzerinden: sihirbaz Bluetooth ayarlarını açar, LE kaydının oluşmasını ve ANCS'nin görünmesini bekler. Uygulama içinden eşleşme Aşama 1'de ayrıca denenir | S1: tek eşleşme klasik ve LE kaydını birlikte kuruyor, ANCS ilk keşifte görünüyor |
| 29 | 16 Ekim | Yakınlık kilidi reklam RSSI'si (`BluetoothLEAdvertisementWatcher`) ve bağlantı durumuyla yapılır. Başlangıç kuralı: 5 saniyelik ortalama −80 dBm'in altına iner veya bağlantı koparsa, 30 saniye sonra kilitle; eşik ayarlanabilir | S1: bağ adresiyle gelen reklam RSSI'si masada −50…−55, uzaklaşınca −78…−92 dBm |
| 30 | 16 Ekim | iPhone'un iOS sürümü ve model adı uygulamasız okunamıyor (DIS'te sadece `iPhone14,5` gibi model kimliği var). Model adı eşleme tablosundan çıkarılır; iOS sürümü Fenpo kanalından gelir, `Hello`'ya bunun için alan eklenmesi S5'teki protokol commit'ine (#23) alınır | S1/S8 |
| 31 | 16 Ekim | Tasarımda değişmesi gerekenler (`design/desktop/desktop.html`, Buğra'nın onayıyla): WhatsApp bildirimindeki "iPhone'da aç" düğmesi kalkar (ANCS'de karşılığı yok); cihaz satırındaki iOS sürümü iOS uygulaması yoksa gösterilmez; "Odak modunu eşitle" açıklaması "Fenpo bildirimlerini susturur" olur (RESEARCH §13) | S1 ve RESEARCH ile çelişen metinler |
| 32 | 16 Ekim | MAP (ve PBAP) için OBEX katmanı Fenpo içinde yazılır; MyPhone sadece referans | S2: OBEX istemci ve MNS sunucusu birkaç yüz satır, iPhone'la okuma, bildirim ve gönderme çalıştı |
| 33 | 16 Ekim | Yeni mesajın ilk gösterimi ANCS'den yapılır; MAP sohbet geçmişi, yanıt, gönderme ve okundu işaretleme için kullanılır | S2: MAP olay raporu gönderen ve metin taşımıyor, `GetMessage` bir kez 33 saniye gecikti |
| 34 | 16 Ekim | Aramalar Aşama 2'de yedek yolla yapılır: gelen arama ANCS `IncomingCall` bildiriminden Ada'da gösterilir, "Yanıtla" ve "Reddet" ANCS aksiyonlarıdır, ses iPhone'da kalır. HFP (`PhoneLineTransportDevice`) beklemeye alınır; kararlı Windows 11'de deneme ve Microsoft'a LAF talebi açık konu. Arama servisi kaynaktan bağımsız arayüzün arkasında kurulur. Ada'daki üç düğmeli arama görünümü iki düğmeye iner (tasarım değişikliği, Buğra'nın onayıyla) | S3: bu makinede `RegisterApp` kaydı almıyor, LAF `Unavailable`. Çıkarılmış anahtarla LAF token'ı üretmek kullanılmaz (#6) |
| 35 | 16 Ekim | Fenpo Kamera'nın medya kaynağı MSIX'in içinden kaydedilmez: DLL herkesin okuyabildiği bir klasöre kopyalanıp HKLM'ye kaydedilir. Bunu, özellik ilk açıldığında yönetici onayıyla (UAC) çalışan küçük, imzalı bir yardımcı yapar; kaldırma da aynı yardımcıyla. Ayrıntı Aşama 4'te | S7: paketli COM sınıfını Frame Server görmüyor (`Class not registered`) |
| 36 | 16 Ekim | Windows uygulaması aşamalara bölünmeden baştan sona yapılır. PLAN'daki aşamalar iç sıralama olarak kalır. Menüler gizlenmez: iPhone uygulaması veya henüz olmayan bir şey gerektiren özellik, ne gerektiğini söyleyen bir durum ekranıyla görünür | Buğra'nın isteği |
| 37 | 16 Ekim | Paket kimliği `Milvasoft.Fenpo`, yayıncı görünen adı "Milvasoft". Geliştirme sürümleri kendinden imzalı `CN=Fenpo Development` sertifikasıyla imzalanır; yayın sertifikası ayrıca belirlenir | Buğra'nın onayı |
| 38 | 16 Ekim | Windows oturumu `design/desktop/desktop.html`'i günceller: uygulamasız (Windows Bluetooth) eşleşme sihirbazı eklenir; #31 ve #34'teki düzeltmeler uygulanır | Buğra'nın onayı; uygulama tasarıma birebir uymalı |
| 39 | 16 Ekim | S5 protokol önerisi (1–11) `e185f5d` ile protokole girdi. 6 haneli yedek eşleşme kodu kaldırıldı; QR okunmazsa PC QR'ı yeniden gösterir. Sayı karşılaştırmalı (SAS) yöntem ileride düşünülebilir. PC, ANCS aboneliğini ve MAP bağlantısını telefona `PcStatus` ile bildirir | S5: kısa kod araya giren saldırgana karşı koruma sağlamıyor; iOS oturumunun ayrıntıları; Buğra'nın onayı |
| 40 | 17 Ekim | Fenpo kimsenin Windows ya da Microsoft hesabı şifresini istemez, saklamaz. Bu yüzden telefonla PC kilidini açma (Face ID/parmak izi ile Credential Provider) yok; kod, protokol (`unlock.proto`, CAPABILITY_UNLOCK 17, Envelope 130–139 ayrıldı) ve üç uygulamadaki ayar kaldırıldı. Kabuk için Fenpo modülü de planda yok | Kilitli oturumu açmak hesabın gerçek şifresini gerektiriyor; Buğra: "kimsenin microsoft hesabının şifresini almayalım lütfen. bu özellik için bu required ise bu özelliği kaldır heryerden". Kabuk modülünü de istemedi |
| 41 | 17 Ekim | Windows uygulaması iki yoldan dağıtılır: **Microsoft Store** (Fenpo Kamera dahil gönderilir; paketi Store imzalar) ve **GitHub Releases** (+ winget, kendi kod imzalama sertifikamızla). Store, Fenpo Kamera'nın yönetici onaylı kurulumunu kabul etmezse Fenpo Kamera yalnızca GitHub sürümünde kalır. İki sürüm aynı koddan çıkar; sadece paket kimliği ve imza farklıdır | Store kurulumu ve güncellemeyi kolaylaştırıyor; GitHub her özelliğin her zaman indirilebildiği yer. Fenpo Kamera'nın HKLM kaydı (#35) Store kurallarına takılabilir. Buğra'nın kararı |

## Açık konular

- Android'in en düşük sürümü: Android oturumu ilk aşamada güncel dağılım verisiyle önerir, Buğra onaylar
