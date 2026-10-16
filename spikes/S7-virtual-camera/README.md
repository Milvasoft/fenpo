# S7: Sanal kamera

## Hedef

Fenpo Kamera'nın Windows tarafı için `MFCreateVirtualCamera` ile bir sanal kamera kurmak: .NET 10 Native AOT ile yazılmış bir medya kaynağı (COM) Frame Server içinde çalışabiliyor mu, ve en önemlisi **MSIX paketiyle kayıt yeterli mi**, yoksa ayrı, yönetici yetkili bir kurulum mu gerekiyor (RESEARCH §8).

## Yapılanlar

- `upstream/`: [smourier/VCamNetSample](https://github.com/smourier/VCamNetSample) (MIT, commit `5f2e41c`) içinden `VCamNetSampleSourceAOT` medya kaynağı ve bir sürüm dosyası, `LICENSE` ile birlikte. Tek değişiklik: Debug derlemesi için hazır gelen ikili `AotNetComHost` DLL'leri ve onları kopyalayan MSBuild hedefi çıkarıldı. Kaynak yalnızca Release'de, Native AOT ile kaynaktan derleniyor.
- `VCamHost/`: "Fenpo S7" adlı sanal kamerayı oluşturan (`MFCreateVirtualCamera`, `Session` ömrü, `CurrentUser` veya `--all-users`), başlatan ve isteğe bağlı olarak `MediaCapture` ile bir kare yakalayıp PNG'ye yazan konsol uygulaması (DirectNAot.Extensions).
- `VCamHost/Package/AppxManifest.xml`: medya kaynağını `com:SurrogateServer` / `com:Class` ile paketli COM sınıfı olarak bildiren MSIX manifesti.
- `Build-SignedMsix.ps1`: kaynağı AOT ile yayınlar, VCamHost'u derler, ikisini S3'teki `CN=Fenpo Development` sertifikasıyla imzalı bir MSIX'e koyar ve kurar.
- `Register-Hklm.ps1`: klasik yol. DLL'i `C:\ProgramData\Fenpo\S7VCam`'e kopyalar ve `regsvr32` ile HKLM'ye kaydeder (yönetici gerekir). `-Unregister` ile geri alınır.

```powershell
.\spikes\S7-virtual-camera\Build-SignedMsix.ps1
FenpoS7 --seconds 3 --snapshot C:\temp\s7.png
```

## Sonuç (16 Ekim 2026, gece, Windows 11 26H2)

| Deneme | `MFCreateVirtualCamera` | `IMFVirtualCamera.Start` |
|---|---|---|
| Kayıt yok, paketlenmemiş | `S_OK` | `0x80040154` Class not registered (beklenen) |
| İmzalı MSIX, medya kaynağı `com:SurrogateServer/com:Class` ile paket içinde, `CurrentUser` | `S_OK` | **`0x80040154` Class not registered** |
| HKLM kaydı (`Register-Hklm.ps1`, DLL `C:\ProgramData\Fenpo\S7VCam`'de), paketlenmemiş `VCamHost` | `S_OK` | **`S_OK`**: ilk seferde 5 s, sonra 60 ms |

- Medya kaynağı .NET 10 Native AOT ile 5,5 MB'lık tek bir DLL olarak derleniyor. AOT için `vswhere.exe` PATH'te olmalı (`C:\Program Files (x86)\Microsoft Visual Studio\Installer`), yoksa link adımı düşüyor.
- **MSIX içindeki paketli COM sınıfını Frame Server görmüyor.** Bu, VCamNetSample'ın notuyla uyumlu: kaynağı Frame Server (Local Service) ve Frame Server Monitor (Local System) yüklüyor. Paketli COM kaydı ise paketi kuran kullanıcıya özel.
- **HKLM kaydıyla çalışıyor** (16 Ekim sabahı): kamera Windows'ta "Fenpo S7 (Windows Virtual Camera)" adıyla görünüyor; Windows adın sonuna "(Windows Virtual Camera)" ekliyor. `MediaCapture` ile alınan kare: 1280×960, RGB32, CPU yolu ([snapshot-hklm.png](snapshot-hklm.png)). Yani .NET 10 Native AOT ile yazılmış bir medya kaynağı Frame Server içinde sorunsuz çalışıyor.
- `Session` ömrüyle kamera sadece `VCamHost` çalışırken var; süreç bitince listeden kalkıyor.

### Temizlik

Kaydı ve DLL'i kaldırmak için yönetici PowerShell'de: `& '...\spikes\S7-virtual-camera\Register-Hklm.ps1' -Unregister`. MSIX test paketi: `Get-AppxPackage Fenpo.Spikes.S7VCam | Remove-AppxPackage`.

## Karar (şimdilik)

- **Fenpo Kamera, MSIX'in kendi başına kurabileceği bir bileşen değil.** Medya kaynağı DLL'i herkesin okuyabildiği bir yere (`%ProgramFiles%\Fenpo\Camera` gibi) kopyalanıp HKLM'ye kaydedilmeli; bu da yönetici izni demek.
- Ürün için önerilen yol: Kamera özelliği ilk açıldığında Fenpo, imzalı ve tek iş yapan küçük bir yardımcıyı yönetici onayıyla (UAC) çalıştırır; yardımcı DLL'i kopyalar ve kaydeder. Kaldırma da aynı yardımcıyla yapılır. Alternatifi ayrı bir MSI. İkisi de Aşama 4'te netleşir. Paketli bir sürecin yükseltilmiş haliyle HKLM'ye yazabildiği ayrıca doğrulanmalı.
- Medya kaynağının C# (Native AOT) ile yazılması kararı (TECH-STACK) **kesinleşti**: HKLM kaydıyla Frame Server içinde görüntü verdi. C++'a geçmeye gerek yok.

## Ürün koduna etkisi

1. `Fenpo.VirtualCamera` ayrı bir Native AOT projesi olarak kalır ama MSIX'e kaydını bırakmaz; kurulumu ayrı ve yönetici onaylı.
2. MSIX kaldırılınca kamera kaydı kendiliğinden gitmez; kaldırma yolu yardımcıya bağlanır ve kurulum dokümanına yazılır.
3. `Session` ömrüyle kamera, Fenpo.exe kapanınca kaybolur. Aşama 4'te `System` ömrü ve "her zaman görünür kamera" seçeneği değerlendirilir.
4. Frame Server ve Fenpo.exe arasındaki görüntü aktarımı (paylaşımlı bellek veya paylaşımlı doku) bu spike'ta yok; Aşama 4'ün konusu.
