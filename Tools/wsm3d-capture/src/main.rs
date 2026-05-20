use image::{ImageBuffer, RgbaImage};
use std::env;
use std::mem::size_of;
use std::path::Path;
use windows::Win32::Foundation::{CloseHandle, BOOL, HWND, LPARAM, MAX_PATH, RECT};
use windows::Win32::Graphics::Gdi::{
    BI_RGB, BITMAPINFO, BITMAPINFOHEADER, BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, DIB_RGB_COLORS, DeleteDC,
    DeleteObject, GetDIBits, GetWindowDC, HGDIOBJ, PrintWindow, SelectObject, SRCCOPY,
};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};
use windows::Win32::UI::WindowsAndMessaging::{
    EnumWindows, GetClientRect, GetWindow, GetWindowRect, GetWindowTextLengthW, GetWindowThreadProcessId, IsWindow,
    PW_CLIENTONLY, GW_OWNER,
};

fn main() {
    let mut args = env::args();
    let program = args.next().unwrap_or_else(|| "wsm3d-capture".to_string());
    let process_name = match args.next() {
        Some(arg) => arg,
        None => {
            eprintln!("usage: {program} <process-name> <output-png>");
            std::process::exit(1);
        }
    };
    let output_path = match args.next() {
        Some(arg) => arg,
        None => {
            eprintln!("usage: {program} <process-name> <output-png>");
            std::process::exit(1);
        }
    };

    if args.next().is_some() {
        eprintln!("usage: {program} <process-name> <output-png>");
        std::process::exit(1);
    }

    match run_capture(&process_name, &output_path) {
        Ok(path) => {
            println!("Saved capture to {}", path);
        }
        Err(err) => {
            eprintln!("capture failed: {err}");
            std::process::exit(1);
        }
    }
}

fn run_capture(process_name: &str, output_path: &str) -> Result<String, String> {
    let normalized_name = normalize_process_name(process_name);
    let pids = find_process_ids(&normalized_name)?;
    if pids.is_empty() {
        return Err(format!("no running process matches '{}'", process_name));
    }

    let hwnd = find_main_window_by_process_ids(&pids)
        .ok_or_else(|| format!("no top-level window found for '{}'", process_name))?;
    let image = capture_window_client_area(hwnd)?;

    if let Some(parent) = Path::new(output_path).parent() {
        std::fs::create_dir_all(parent).map_err(|e| format!("create output directory: {e}"))?;
    }
    image.save(output_path).map_err(|e| format!("save png: {e}"))?;
    Ok(output_path.to_string())
}

fn normalize_process_name(raw: &str) -> String {
    let mut name = raw.to_ascii_lowercase();
    if !name.ends_with(".exe") {
        name.push_str(".exe");
    }
    name
}

fn find_process_ids(process_name: &str) -> Result<Vec<u32>, String> {
    let snapshot = unsafe {
        CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0).map_err(|e| format!("CreateToolhelp32Snapshot: {e}"))?
    };

    let mut entry = PROCESSENTRY32W {
        dwSize: size_of::<PROCESSENTRY32W>() as u32,
        ..Default::default()
    };

    let mut ids = Vec::new();
    if unsafe { Process32FirstW(snapshot, &mut entry).as_bool() } {
        loop {
            let exe = wide_to_string(&entry.szExeFile);
            if exe == *process_name {
                ids.push(entry.th32ProcessID);
            }

            if !unsafe { Process32NextW(snapshot, &mut entry).as_bool() } {
                break;
            }
        }
    }

    unsafe {
        let _ = CloseHandle(snapshot);
    }
    Ok(ids)
}

#[derive(Default)]
struct SearchState {
    target_pids: Vec<u32>,
    best_hwnd: Option<HWND>,
    best_area: i64,
}

fn find_main_window_by_process_ids(target_pids: &[u32]) -> Option<HWND> {
    let mut state = SearchState {
        target_pids: target_pids.to_vec(),
        ..Default::default()
    };
    unsafe {
        let _ = EnumWindows(Some(enum_windows_proc), LPARAM(&mut state as *mut SearchState as isize));
    }
    state.best_hwnd
}

unsafe extern "system" fn enum_windows_proc(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let state = &mut *(lparam.0 as *mut SearchState);

    if !IsWindow(hwnd).as_bool() || !is_main_window(hwnd) {
        return BOOL(1);
    }

    let mut pid = 0u32;
    let _ = GetWindowThreadProcessId(hwnd, &mut pid);
    if !state.target_pids.contains(&pid) {
        return BOOL(1);
    }

    let mut rect = RECT::default();
    if !GetWindowRect(hwnd, &mut rect).as_bool() {
        return BOOL(1);
    }
    let width = (rect.right - rect.left) as i64;
    let height = (rect.bottom - rect.top) as i64;
    let area = width.saturating_mul(height);
    if area <= 0 {
        return BOOL(1);
    }

    if state.best_hwnd.is_none() || area > state.best_area {
        state.best_area = area;
        state.best_hwnd = Some(hwnd);
    }

    BOOL(1)
}

fn is_main_window(hwnd: HWND) -> bool {
    if !IsWindow(hwnd).as_bool() {
        return false;
    }
    if GetWindow(hwnd, GW_OWNER).0 != 0 {
        return false;
    }
    GetWindowTextLengthW(hwnd) > 0
}

fn capture_window_client_area(hwnd: HWND) -> Result<RgbaImage, String> {
    let mut rect = RECT::default();
    if !unsafe { GetClientRect(hwnd, &mut rect).as_bool() } {
        return Err("failed to get client rect".into());
    }
    let width = rect.right - rect.left;
    let height = rect.bottom - rect.top;
    if width <= 0 || height <= 0 {
        return Err("invalid client area".into());
    }

    unsafe {
        let hdc_window = GetWindowDC(hwnd);
        if hdc_window.0 == 0 {
            return Err("failed to get window DC".into());
        }
        let hdc_mem = CreateCompatibleDC(Some(hdc_window));
        if hdc_mem.0 == 0 {
            let _ = windows::Win32::Foundation::ReleaseDC(hwnd, hdc_window);
            return Err("failed to create compatible DC".into());
        }

        let hbitmap = CreateCompatibleBitmap(hdc_window, width, height);
        if hbitmap.0 == 0 {
            let _ = DeleteDC(hdc_mem);
            let _ = windows::Win32::Foundation::ReleaseDC(hwnd, hdc_window);
            return Err("failed to create compatible bitmap".into());
        }

        let old = SelectObject(hdc_mem, HGDIOBJ(hbitmap.0));
        let printed = PrintWindow(hwnd, hdc_mem, PW_CLIENTONLY).as_bool();
        if !printed {
            if !BitBlt(hdc_mem, 0, 0, width, height, hdc_window, 0, 0, SRCCOPY).as_bool() {
                let _ = SelectObject(hdc_mem, old);
                let _ = DeleteObject(HGDIOBJ(hbitmap.0));
                let _ = DeleteDC(hdc_mem);
                let _ = windows::Win32::Foundation::ReleaseDC(hwnd, hdc_window);
                return Err("both PrintWindow and BitBlt failed".into());
            }
        }

        let mut bmi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: width,
                biHeight: -height,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB,
                biSizeImage: 0,
                biXPelsPerMeter: 0,
                biYPelsPerMeter: 0,
                biClrUsed: 0,
                biClrImportant: 0,
            },
            bmiColors: [Default::default(); 1],
        };

        let byte_len = (width as usize) * (height as usize) * 4;
        let mut bytes = vec![0u8; byte_len];
        let got = GetDIBits(
            hdc_mem,
            hbitmap,
            0,
            height as u32,
            Some(bytes.as_mut_ptr() as *mut _),
            &mut bmi,
            DIB_RGB_COLORS,
        );

        let _ = SelectObject(hdc_mem, old);
        let _ = DeleteObject(HGDIOBJ(hbitmap.0));
        let _ = DeleteDC(hdc_mem);
        let _ = windows::Win32::Foundation::ReleaseDC(hwnd, hdc_window);

        if got == 0 {
            return Err("GetDIBits returned 0".into());
        }

        for pixel in bytes.chunks_exact_mut(4) {
            pixel.swap(0, 2);
            if pixel[3] == 0 {
                pixel[3] = 255;
            }
        }

        ImageBuffer::from_vec(width as u32, height as u32, bytes).ok_or_else(|| "failed to build image buffer".into())
    }
}

fn wide_to_string(buf: &[u16; MAX_PATH as usize]) -> String {
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..len]).to_ascii_lowercase()
}
