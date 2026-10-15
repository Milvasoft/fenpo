# Fenpo

Telefon ile Windows arasında, Mac + iPhone uyumuna olabildiğince yakın bir deneyim. Üç uygulama var: **Fenpo for Windows** (.NET 10, WinUI 3, MSIX), **Fenpo for iPhone** (Swift 6.4, SwiftUI, iOS 18+) ve **Fenpo for Android** (Kotlin, Jetpack Compose). Açık kaynak, sunucusuz, her şey yerel.

## Önce bunları oku

1. `docs/DECISIONS.md`: alınmış bütün kararlar. Bir kararla çelişen bir şey yapmadan önce Buğra'ya sor.
2. `docs/PLAN.md`: hedef, Mac özelliklerinin Fenpo karşılıkları, aşamalar
3. `docs/ARCHITECTURE.md`: dört bağlantı yolu, süreçler, projeler, protokol, eşleşme ve güvenlik
4. `protocol/`: üç uygulamanın ortak sözleşmesi (`protocol/README.md`)
5. `docs/TECH-STACK.md`: her teknoloji seçimi ve neden
6. `docs/RESEARCH.md`: neyin mümkün neyin olmadığı, kaynaklarıyla. Bir şeyin "yapılamaz" olduğunu söylemeden önce buraya bak.
7. `design/`: Windows (`design/desktop/desktop.html`), iPhone (`design/mobile/mobile.html`) ve logo (`design/logo/logos.html`) tasarımları. Arayüz bunlara birebir uymalı: renkler, metinler, yerleşim. Logo **Çekirdek** (`design/logo/svg/1-cekirdek.svg`).

## Şu anki durum

Planlama, tasarım ve protokol v1 taslağı bitti; uygulama kodu yok. İş üç oturuma bölündü (`docs/DECISIONS.md` #18–20, başlatma promptları `docs/KICKOFF.md`):

| İş kolu | Klasör ve dal | Dokunabildiği yerler |
|---|---|---|
| Windows (ana) | `fenpo`, `main` | `windows/`, `spikes/`, `protocol/`, `docs/`, `.github/workflows/windows.yml` |
| iPhone | `fenpo-ios` worktree, `ios` | `ios/`, `docs/ios/`, `.github/workflows/ios.yml` |
| Android | `fenpo-android` worktree, `android` | `android/`, `docs/ANDROID.md`, `docs/android/`, `design/android/`, `.github/workflows/android.yml` |

Kendi alanın dışındaki bir dosyayı değiştirmen gerekiyorsa önce Buğra'ya söyle. Protokol değişikliği her zaman ayrı bir `protocol: ...` commit'i.

Sıradaki işler **Aşama 0 spike'ları** (`docs/PLAN.md`).

Her spike `spikes/S<n>-<ad>/` altında küçük bağımsız bir proje olur ve bir `README.md` bırakır: ne denendi, ne oldu, karar ne. Spike kodu ürün koduna kopyalanmaz, ürün kodu temiz yazılır.

## Planlanan repo düzeni

```
fenpo/
├── CLAUDE.md, README.md, .editorconfig, .gitignore
├── docs/          plan, mimari, teknoloji, araştırma
├── design/        HTML tasarımlar ve logo SVG'leri
├── protocol/      fenpo/v1/*.proto (tek kaynak, C# ve Swift buradan üretilir)
├── spikes/        Aşama 0 denemeleri
├── windows/       Fenpo.slnx, src/, native/, tests/  (bkz. ARCHITECTURE.md)
├── ios/           project.yml (XcodeGen), Fenpo/, FenpoShare/, FenpoWidgets/, FenpoFiles/, FenpoKit/
├── android/       Gradle projesi (yapı docs/ANDROID.md'de belirlenecek)
└── .github/workflows/  windows.yml, ios.yml, android.yml
```

## Çalışma kuralları

- **Yarım iş teslim etme.** Bir özelliği "bitti" saymadan önce kendi tarafında uçtan uca doğrula: protokol mesajı → işleyici → arayüz → ayar → iki tema → iki dil. Karşı taraf henüz hazır değilse bunu açıkça yaz ("Windows tarafı bekleniyor").
- **Mevcut konvansiyona uy.** Yeni dosya yazmadan önce aynı klasördeki benzer dosyaya bak; isimlendirme, klasör düzeni, yorum stili oradan gelir. Yeni desen icat etme, aynı işi yapan ikinci bir sınıf veya uç yaratma.
- **Derle ve test et.** Windows tarafında her değişiklikten sonra `dotnet build` ve ilgili testler. Ayrıştırıcılar (ANCS, AMS, OBEX, MAP) gerçek cihazdan kaydedilmiş bayt dizileriyle test edilir; bu kayıtlar `tests/**/Fixtures/` altında durur.
- **Gerçek cihaz gerektiren şeyi uydurma.** Bluetooth davranışı emülatörde doğrulanamaz. Cihazda denenmemiş bir şeyi "çalışıyor" diye raporlama; "derleniyor, cihazda denenmedi" de.
- **Tema ve dil her ekranda.** Üç uygulamada da Görünüm (Sistem, Açık, Koyu) ve Dil (Sistem dili, Türkçe, English) ayarı var. Yeni bir ekran, pencere, bildirim veya widget ancak iki temada ve iki dilde doğrulandıktan sonra bitmiş sayılır. Renkler tema kaynaklarından gelir, kodda sabit renk yazılmaz; metinler kaynak dosyalarından gelir, kodda sabit metin yazılmaz.
- **Apple ve Microsoft markalarını arayüzde kullanma.** "AirDrop", "AirPlay", "Handoff", "Continuity" Fenpo'nun arayüzünde geçmez; dokümanlarda kıyas için geçebilir.

## C# kod stili

Kök dizindeki `.editorconfig` (Milvaion'dan) geçerli. Ayrıca:

- Dosya kapsamlı namespace, `using`'ler namespace dışında.
- Bağımlılıklar primary constructor ile alınır ve **mutlaka** aşağıdaki desenle alana atanır:

  ```csharp
  /// <summary>
  /// Reads notifications from the iPhone through Apple Notification Center Service.
  /// </summary>
  public sealed class AncsClient(IGattConnection connection, ILogger<AncsClient> logger) : IAncsClient
  {
      private readonly IGattConnection _connection = connection;
      private readonly ILogger<AncsClient> _logger = logger;

      /// <summary>
      /// Starts listening to the Notification Source characteristic.
      /// </summary>
      /// <param name="cancellationToken">Cancellation token.</param>
      public async Task StartAsync(CancellationToken cancellationToken = default)
      {
          ...
      }
  }
  ```

- Public tip ve üyelerde XML doc yorumu (`/// <summary>`), İngilizce.
- Özel alanlar `_camelCase`, async metotlar `Async` ile biter, `CancellationToken` son parametre.
- Satır sonu CRLF, girinti 4 boşluk, süslü parantez her zaman yeni satırda.
- Kod, log mesajları ve yorumlar İngilizce; kullanıcıya görünen metinler `Strings/tr-TR/Resources.resw` ve `Strings/en-US/Resources.resw` içinde, ikisi her zaman birlikte güncellenir.
- Tema: renkler `ThemeDictionaries` (Light, Dark) içindeki kaynaklardan gelir; ana pencere, tepsi paneli ve Ada aynı tema servisini dinler.

## Swift kod stili

- Swift 6 sıkı eşzamanlılık açık. Model katmanında `@Observable`, arka plan işleri `actor` ile.
- Dış bağımlılık sadece `swift-protobuf`. Yenisi gerekiyorsa önce sor.
- Proje dosyası XcodeGen `project.yml`; `.xcodeproj` elle düzenlenmez ve repoya girmez.
- Kullanıcıya görünen metinler String Catalog'da (`Localizable.xcstrings`), Türkçe ve İngilizce; uzantılar da aynı katalog düzenini kullanır.
- Tema: renkler Asset Catalog'da Any/Dark çeşitleriyle tanımlanır, kodda sabit renk yok.
- `FenpoKit` ikiye ayrılır: Apple çatılarına bağlı olmayan çekirdek (protokol, çerçeveleme, QR ayrıştırma, durum makineleri) ve Apple'a özel kısım (CoreBluetooth, Network.framework). Çekirdek, Windows'taki Swift araç zinciriyle de derlenip test edilebilir olmalı.

## Kotlin kod stili

- Android oturumu ilk aşamada `docs/ANDROID.md` içinde kod stilini ve modül yapısını önerir; Buğra onaylayınca buraya işlenir.
- Kullanıcıya görünen metinler `values/strings.xml` (English) ve `values-tr/strings.xml` (Türkçe); tema Material 3 açık/koyu renk şemalarından, kodda sabit renk yok.

## Tasarım sözlüğü

| Ad | Değer | Kullanım |
|---|---|---|
| Mürekkep | `#1B1F3B` | Metin, koyu yüzeyler |
| Çiçek | `#E8577E` | Marka vurgusu, iOS tint (açık zeminde metin için `#C93A63`) |
| Yaprak | `#2F8F6B` | Bağlı durumu, başarı |
| Alacakaranlık | `#4E54C8` | İkincil vurgu |
| Cam | `#DCE6F5` | Açık yüzeyler |
| Pervaz | `#F6F7FB` | Açık zemin |

Uygulamalar sistem fontunu kullanır (Windows'ta Segoe UI Variable, iOS'ta SF Pro). Gabarito sadece logo ve marka yüzeylerinde. Kırmızı sadece reddet ve hata için.

Arayüz metinleri: düz fiiller, cümle düzeni büyük harf, düğme ne yapacaksa onu söyler ("Gönder", "Yanıtla"), hata mesajları özür dilemez ama nasıl düzeltileceğini söyler.

## Ortam

- Windows tarafı Buğra'nın Windows 11 makinesinde derlenir: .NET 10 SDK, Windows App SDK 2.5.x, Visual Studio 2026 (MSIX ve WinUI araçları için).
- **Mac yok.** iOS kodu Windows'ta yazılır. Derleme ve birim testleri GitHub Actions macOS runner'ında (`.github/workflows/ios.yml`) koşar; CI yeşil olmadan iOS kodu "derleniyor" sayılmaz. Cihaz testleri ortağın Mac'i ve TestFlight ile kilometre taşlarında yapılır (`docs/ios/TESTING.md`).
- Android tarafı Buğra'nın makinesinde Android Studio ile derlenir. Bluetooth için gerçek Android telefon gerekir.
- Bluetooth testleri için bilgisayarın Bluetooth adaptörü çevre birim rolünü desteklemeli (`BluetoothAdapter.IsPeripheralRoleSupported`).
