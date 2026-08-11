TOOL TIKTOK V12.5 - HYBRID V12 MANAGER + V11.5 WORKERS

KIEN TRUC
- Phan dau: Manager/multi-profile/CDP port/giao dien thanh tren lay tu V12.
- Moi profile = 1 process ToolTikTokWorkerV125 rieng + 1 Chrome profile + 1 CDP port rieng.
- Worker dung giao dien va logic V11.5: AutomationEngine, ImageMatcher, ChromeController, SettingsService va cac service V11.5.
- Form V11.5 duoc gan vao tab Manager bang Win32 SetParent; co nut Tach/Gan V11.5 neu can debug.
- Khong port AutomationEngine V11.5 sang engine V12 va khong dung AutomationEngine V12.

NHUNG PHAN V11.5 DUOC GIU NGUYEN
- V115Core/Services/AutomationEngine.cs: nguyen ban V11.5.
- V115Core/Services/ImageMatcher.cs: nguyen ban V11.5.
- V115Core/Services/ChromeController.cs: nguyen ban V11.5.
- V115Core/Services/SettingsService.cs: nguyen ban V11.5.
- Models/Services/Utils V11.5 con lai giu nguyen.
- MainForm.cs chi them bridge managed-mode: data root rieng/profile, khoa profile selector, tat global hotkey de nhieu worker khong tranh nhau, va gan startup profile/port tu Manager. Logic automation khong bi viet lai.

PHAN V12 GIU LAI
- TikTokProfileService + profile catalog.
- CDP port rieng cho tung profile (port da gan va duy nhat duoc giu on dinh).
- Quan ly nhieu worker/profile song song.
- Thanh cong cu Manager: them/profile co san/doi ten/xoa/mo/chay tat ca/dung tat ca.
- Logger/theme/profile safety can thiet.

DU LIEU
- Chrome profile: D:\TOOL V2\TikTokProfiles\<profile>\chrome_profile
- Config V11.5 rieng tung profile: dist_v125\profiles\<profile>
- Neu dist_v12 nam canh dist_v125, V12.5 tu nhap profiles.json va copy cac file config profile V12 con thieu truoc khi khoi dong worker.
- Profile moi duoc nap defaults V11.5. File noi dung mac dinh de trong; profile cu giu noi dung da co.

BUILD / CHAY
1. May Windows cai .NET 8 SDK.
2. Chay BUILD_V12_5.bat.
3. Mo dist_v125\ToolTikTokManagerV125.exe.
Hoac chay RUN_V12_5_DEV.bat de build + mo Manager.

LUU Y
- Profile selector va nut them/doi/xoa profile ben trong giao dien V11.5 bi khoa khi worker do Manager quan ly. Lam cac thao tac profile o thanh tren Manager.
- Global F8/F9/Esc cua V11.5 bi tat trong managed worker de cac process khong tranh hotkey; nut Start/Pause/Stop tren giao dien V11.5 van hoat dong.
- Giao dien V11.5 la process rieng duoc embed vao tab. Neu may/DPI nao do hien thi khong on, bam "Tach V11.5" de dung nhu cua so V11.5 binh thuong.
