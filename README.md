# Klasör Geçiş

[English](#english) · [Türkçe](#türkçe)

## English

**Reach a folder you already have open in File Explorer directly from an application's Open or Save dialog.**

Klasör Geçiş reduces repeated folder navigation when opening, saving, exporting or selecting files. If your project folder is already open in Explorer, use one click or **Ctrl+G** to reach it from the file dialog instead of browsing through the same directory tree again.

For example, keep a project folder open while working on a document or an image. When you choose Save As, use the folder bar to reach that folder. Its menu also lets you choose another open Explorer folder, a favorite or an optional recent folder.

**Preview release:** [Download v0.5.0](https://github.com/inceof/klasor-gecis/releases/tag/v0.5.0).
Choose the Setup EXE to install, or the portable ZIP and extract all files.
Keep the included license notices. This is an unsigned build; verify the
SHA-256 file published with the release.

Built with .NET 10 and WinForms, the app runs in the Windows notification area. Version **0.5.0**.

Six built-in languages: **Türkçe, English, 中文（简体）, हिन्दी, Español, العربية**.
Choose a language in **Settings → General → Application language**. The choice
is saved and applied immediately; existing profiles keep Turkish by default.
Arabic uses right-to-left layout. Folder names and paths remain unchanged.

Open a folder in File Explorer, open another application's standard Open/Save
dialog, then click the folder bar or press Ctrl+G. The menu also offers favorite
and recent folders. History and automatic switching are opt-in.

To build on Windows, install .NET SDK 10.0.300 and run `./build.ps1` in PowerShell.
It builds and runs rule/checksum tests, then produces the self-contained
`release/win-x64/KlasorGecis.exe`. Building the six-language installer additionally
requires Inno Setup 6; compile `installer/KlasorGecis.iss` after building the EXE.

Custom file pickers, elevated targets, real network shares and mixed-DPI setups
may behave differently. Controlled Windows Sandbox install/reinstall/uninstall
and 0.4.0-to-0.5.0 upgrade checks passed. The final notice-only setup rebuild
was checked by extracting and matching its files. Visual installer, physical
device/accessibility and native-speaker translation review remain unvalidated. See the Turkish development
instructions below for test commands. Source code is [MIT licensed](LICENSE);
third-party installer translation credits remain in `installer/Languages`.

### Features

- A compact folder bar attached to standard Windows Open/Save dialogs.
- Open Explorer folders, favorites and optional recent folders in one menu.
- A configurable global shortcut; the default is **Ctrl+G**.
- Controls designed to preserve the typed filename, file type and input focus during navigation.
- Optional automatic switching when returning from Explorer to the file dialog.
- Optional history and startup with Windows.

### Download and install

| Download | Use |
| --- | --- |
| [Setup EXE](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/KlasorGecis_Setup_0.5.0_x64.exe) | Installs for the current Windows user without administrator rights. Desktop shortcut and startup tasks are optional. |
| [Portable ZIP](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/KlasorGecis-0.5.0-win-x64-portable.zip) | Extract all files, then run `KlasorGecis.exe`. Keep the included license files. |
| [SHA256SUMS.txt](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/SHA256SUMS.txt) | Compare the SHA-256 of your download with the published value. |

The downloads target **Windows x64** and include the .NET runtime; a separate .NET installation is not required to run the app. The binaries are **unsigned**. Version 0.5.0 is a public preview; see the release notes for its validation limits.

The installer uses `%LOCALAPPDATA%\Programs\KlasorGecis`. To uninstall, exit the app from its tray menu and use Windows **Settings → Apps**. Preferences are retained. Exit the portable app before moving its folder.

### Quick start

1. Start the app; its icon appears in the Windows notification area.
2. Open the folder you want to use in File Explorer.
3. Open a standard **Open**, **Save** or **Save As** dialog in your application.
4. Click the folder bar or press **Ctrl+G**. Use the menu to choose another open folder, favorite or recent folder.

Open **Settings** from the tray menu to change the shortcut, favorites and history. If Ctrl+G is registered by another app, choose a different shortcut under **Settings → Shortcut**. Automatic switching and history are off by default. Use **Exit** in the tray menu to close the app. The installer language and application language are selected separately.

### Privacy and local data

Preferences and diagnostic logs are stored locally in `%LOCALAPPDATA%\KlasorGecis`. Folder history is optional and disabled by default. When enabled, it stores recently used folder paths, the associated application identity and timestamps. History can be cleared from Settings.

Review logs and screenshots before attaching them to an issue; remove personal paths and confidential information.

### Development, feedback and license

Build and test commands are included in the Turkish development section below. Live tests use the mouse and focus and create controlled Explorer windows; exit the running app first and run live groups one at a time.

[GitHub Actions](https://github.com/inceof/klasor-gecis/actions) builds on Windows using SDK 10.0.300, runs rule tests and PowerShell 7/5.1 checksum checks, and retains portable artifacts with license notices for seven days. Native UI and installer acceptance are separate checks.

[Report a bug or suggest an improvement](https://github.com/inceof/klasor-gecis/issues). Include the app and Windows versions, target application, interface language, expected behavior and steps to reproduce. Contributions are welcome; describe the change and relevant validation in your pull request.

The application source is [MIT licensed](LICENSE). Bundled components retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md), `licenses/` and the installer translation credits in `installer/Languages/`.

## Türkçe

**Dosya Gezgini'nde zaten açık olan klasöre, başka bir uygulamanın Aç veya Kaydet penceresinden doğrudan geçin.**

Klasör Geçiş; dosya açarken, kaydederken, dışa aktarırken veya dosya seçerken aynı klasörlere tekrar tekrar gitme ihtiyacını azaltır. Çalıştığınız klasör Gezgin'de açıksa, dosya penceresini tek tıklamayla veya **Ctrl+G** ile o klasöre yönlendirebilirsiniz.

Örneğin bir belge veya görsel üzerinde çalışırken proje klasörünü Gezgin'de açık tutun. Farklı Kaydet dediğinizde klasör satırını kullanarak o klasöre geçin. Menüden diğer açık klasörleri, favorilerinizi veya isteğe bağlı son klasörleri de seçebilirsiniz.

**Ön sürüm:** [v0.5.0 indir](https://github.com/inceof/klasor-gecis/releases/tag/v0.5.0).
Kurulum için Setup EXE'yi, taşınabilir kullanım için ZIP'i seçip tüm dosyaları
çıkarın. Lisans dosyalarını koruyun. Paket kod imzalı değildir; sürüm sayfasındaki
SHA-256 dosyasıyla indirdiğiniz dosyanın özetini karşılaştırabilirsiniz.

Windows Dosya Gezgini'nde açık olan klasöre, başka bir uygulamanın **Aç / Kaydet**
penceresinden tek tıklamayla veya **Ctrl+G** ile geçin.

.NET 10 ve WinForms ile yazılmış, bildirim alanında çalışan bir Windows x64
yardımcısıdır. Güncel uygulama sürümü **0.5.0**.

**Türkçe, English, 中文（简体）, हिन्दी, Español, العربية** dilleri dahildir.
**Ayarlar → Genel → Uygulama dili** alanından seçim yapabilirsiniz. Dil anında
uygulanır ve kaydedilir; mevcut profiller Türkçe kalır. Arapça sağdan sola
düzen kullanır. Klasör adları ve yollar çevrilmez. Setup da altı dili sunar;
kurulum dili ile uygulamada kaydedilen dil birbirinden bağımsızdır.

![Klasör Geçiş ayarları](docs/images/settings.png)

## Özellikler

- Dosya penceresine bağlı, odak almayan klasör geçiş satırı.
- Açık Explorer klasörleri, favoriler ve isteğe bağlı son klasörler menüsü.
- Değiştirilebilir genel kısayol; varsayılan **Ctrl+G**.
- Genel, Kısayol, Favoriler ve Geçmiş ayarları.
- Dosya adı, dosya türü ve giriş odağını korumaya yönelik kontroller.
- İsteğe bağlı otomatik geçiş ve Windows ile başlangıç.

## İndirme ve kurulum

| İndirme | Kullanım |
| --- | --- |
| [Setup EXE](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/KlasorGecis_Setup_0.5.0_x64.exe) | Yönetici yetkisi gerektirmeden mevcut kullanıcıya kurar. Masaüstü kısayolu ve başlangıç seçenekleri isteğe bağlıdır. |
| [Taşınabilir ZIP](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/KlasorGecis-0.5.0-win-x64-portable.zip) | Tüm dosyaları çıkarıp `KlasorGecis.exe` dosyasını çalıştırın. Lisans dosyalarını koruyun. |
| [SHA256SUMS.txt](https://github.com/inceof/klasor-gecis/releases/download/v0.5.0/SHA256SUMS.txt) | İndirdiğiniz dosyanın SHA-256 özetini yayımlanan değerle karşılaştırın. |

Paketler **Windows x64** içindir ve .NET çalışma zamanını içerir; çalıştırmak için ayrıca .NET kurmanız gerekmez. Güncel sürüm bir ön sürümdür; doğrulama sınırları sürüm notlarında açıklanır.

Kurulum yolu `%LOCALAPPDATA%\Programs\KlasorGecis` klasörüdür. Kaldırmak için önce bildirim alanındaki menüden programı kapatın, ardından Windows **Ayarlar → Uygulamalar** bölümünü kullanın. Tercihler korunur. Taşınabilir klasörü taşımadan önce programdan çıkın.

## Kullanım

1. Programı çalıştırın; bildirim alanında klasör simgesi görünür.
2. Dosya Gezgini'nde kullanmak istediğiniz klasörü açın.
3. Başka bir uygulamada standart Aç veya Kaydet penceresini açın.
4. Geçiş satırına tıklayın veya **Ctrl+G** kullanın. Sağdaki menüden başka bir
   klasör, favori veya son klasör seçebilirsiniz.

Tercihler bildirim alanındaki **Ayarlar…** komutundadır. Otomatik geçiş ve
geçmiş başlangıçta kapalıdır. Programı **Çıkış** komutuyla kapatabilirsiniz.

Ctrl+G başka bir uygulamada kayıtlıysa **Ayarlar → Kısayol** bölümünden farklı bir kısayol seçin.

## Kaynaktan derleme

Windows x64 ve **.NET SDK 10.0.300** gerekir (`global.json`). Proje klasöründe PowerShell açın:

```powershell
.\build.ps1
```

Betik derler, kural/regresyon ve SHA-256 kontrollerini çalıştırır ve .NET çalışma
zamanını içeren taşınabilir `release\win-x64\KlasorGecis.exe` üretir.
Kaynak klasörünün adı ve konumu sabit değildir. GUI testleri otomatik başlamaz.

Kurulum dosyası için önce yukarıdaki EXE'yi derleyin; ardından **Inno Setup 6** ile:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" .\installer\KlasorGecis.iss
```

Derleyici farklı konumdaysa komuttaki ISCC.exe yolunu değiştirin.
Setup `release\setup` altında oluşur; mevcut kullanıcıya kurulur.
Derlenmiş EXE ve setup dosyaları kaynak deposunun dışında tutulur.

## Testler ve sınırlar

```powershell
dotnet build .\tests\KlasorGecis.Tests\KlasorGecis.Tests.csproj -c Release
dotnet run --project .\tests\KlasorGecis.Tests\KlasorGecis.Tests.csproj -c Release --no-build
```

Canlı testler için çalışan Klasör Geçiş örneğini kapatın; aşağıdaki komutu ayrı
çalıştırın. Test fareyi ve odağı kullanır, kontrollü Explorer pencereleri oluşturur.
Canlı test gruplarını aynı anda çalıştırmayın.

```powershell
$test = '.\tests\KlasorGecis.Tests\bin\Release\net10.0-windows\KlasorGecis.Tests.exe'
Start-Process -FilePath $test -ArgumentList '--source-suite --out verification\sources.json' -Wait
```

Standart Windows Aç/Kaydet pencereleri hedeflenir. Özel dosya seçiciler, sanal
klasörler ve yönetici yetkili uygulamalar farklı davranabilir. Gerçek UNC,
fiziksel karma DPI, ekran okuyucu ve kurulum/yükseltme/kaldırma kabulü tüm
senaryolar için doğrulanmış değildir. Hata bildirirken Windows sürümünüzü,
hedef uygulamayı ve yeniden üretme adımlarını belirtin; kişisel dosya yollarını
ve gizli bilgileri paylaşmadan önce çıkarın.

## Gizlilik, veri ve lisans

Tercihler `%LOCALAPPDATA%\KlasorGecis` altında saklanır. İzin verilirse son
klasörler, ilgili uygulama kimliği ve zaman bilgisi kaydedilir; dosya adları ve yazılan metinler geçmişe
eklenmez. Tanılama kaydı da bu klasördedir.

Geçmiş varsayılan olarak kapalıdır ve Ayarlar'dan temizlenebilir. Günlük veya ekran görüntüsü paylaşmadan önce kişisel yolları ve gizli bilgileri çıkarın.

Bu projenin kaynak kodu [MIT lisansı](LICENSE) ile sunulur.
.NET ve derleme araçlarının lisansları kendi bileşenlerine aittir.
Katkılar ve hata bildirimleri memnuniyetle karşılanır.

[Hata bildirin veya geliştirme önerin](https://github.com/inceof/klasor-gecis/issues). Uygulama ve Windows sürümünü, hedef programı, arayüz dilini, beklediğiniz davranışı ve tekrar adımlarını belirtin. Pull request açıklamasında değişikliği ve ilgili doğrulamayı yazın.

Dağıtımlardaki `LICENSE`, `THIRD-PARTY-NOTICES.md` ve `licenses/` dosyalarını taşınabilir EXE ile birlikte saklayın.

## Otomatik kontroller

GitHub Actions her değişiklikte Windows derlemesini ve mevcut testleri çalıştırır.
SDK 10.0.300 ve PowerShell 7/5.1 dosya özeti kontrolleri kullanılır; lisansları içeren taşınabilir çıktılar yedi gün saklanır. GUI/kurulum testleri bu akışa dahil değildir.

