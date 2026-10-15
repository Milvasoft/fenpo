# S0: Geliştirme ortamı

## Hedef

Windows tarafının araçlarını ve Bluetooth adaptörünün rollerini spike'lardan önce doğrulamak.

## Yapılanlar

- `dotnet --list-sdks`, Visual Studio bileşenleri (`vswhere`), Windows App Runtime paketleri, Windows sürümü, Geliştirici Modu.
- `BluetoothRoles/`: varsayılan adaptörün `BluetoothAdapter` özelliklerini ve eşleşmiş cihazları yazan konsol programı.

```bash
dotnet run -c Release --project spikes/S0-environment/BluetoothRoles
```

## Sonuç (15 Ekim 2026)

| Konu | Durum |
|---|---|
| Windows | Windows 11 Pro 26H2, build 26300.9550. En düşük hedef 22621'in üstünde |
| .NET | SDK 10.0.401 (çalışma zamanı 10.0.12) ve 9.0.314. 10.0.13 çıkmış, güncellemek isteğe bağlı |
| Visual Studio 2026 | 18.10.3 Community. C++ masaüstü iş yükü ve Windows 11 SDK 26100 var. **.NET masaüstü geliştirme ve WinUI uygulama geliştirme iş yükleri yok** |
| Windows App SDK | Çalışma zamanı 2.5.1 (x64, x86) kurulu. NuGet paketi projelerde geri yüklemeyle gelir |
| Geliştirici Modu | Kapalı. İmzasız MSIX'i kaydetmek (S3, S7, uygulama) için açık olmalı |
| Bluetooth adaptörü | MediaTek (USB). Klasik ve LE var |
| `IsCentralRoleSupported` | True |
| `IsPeripheralRoleSupported` | True |
| LE Secure Connections | True (klasik Secure Connections da True) |
| Genişletilmiş reklam | True, `MaxAdvertisementDataLength` 312 bayt; reklam yükü donanıma aktarılabiliyor |
| Eşleşmiş iPhone | Hem LE hem klasik olarak zaten eşleşmiş. LE kaydındaki adres klasik adresten farklı (S1'de bakılacak) |
| Phone Link | Çalışıyor (`PhoneExperienceHost`) |

## Karar

- Adaptör çevre birim rolünü destekliyor: S4'ün Windows tarafı (`GattServiceProvider`) bu makinede denenebilir.
- 312 baytlık genişletilmiş reklam 128 bit servis UUID'sine yer açıyor, ama iPhone'un arka planda genişletilmiş reklamı tarayıp taramadığı S4'te görülecek; ilk deneme eski (31 bayt) reklamla yapılır.
- Spike'lardan önce eksik iş yükleri kurulur ve Geliştirici Modu açılır.

## Ürün koduna etkisi

- Fenpo'nun ilk açılışında `IsPeripheralRoleSupported` kontrol edilmeli; desteklemeyen adaptörde ③ kanalı kapatılır ve kullanıcıya söylenir.
- S1 öncesi Phone Link kapatılmalı; ürün de Phone Link çalışıyorsa bunu algılayıp uyarmalı.
