//! Desktop guard example — blocks the program unless the session is valid.
//!
//! Env-driven so it runs unattended:
//!   LUMENAUTH_NAME / LUMENAUTH_OWNERID / LUMENAUTH_VERSION / LUMENAUTH_SECRET  (required)
//!   LUMENAUTH_URL      optional — override for self-hosting / local dev
//!   LUMENAUTH_EMAIL / LUMENAUTH_PASSWORD
//!   LUMENAUTH_LICENSE  if set, register instead of login
//!   LUMENAUTH_FILEID   if set, download that file after auth
//!
//! Run:  cargo run --example guard

use std::env;

use lumenauth::{hwid, LumenAuthClient};

fn main() {
    let name = env::var("LUMENAUTH_NAME").expect("set LUMENAUTH_NAME");
    let ownerid = env::var("LUMENAUTH_OWNERID").expect("set LUMENAUTH_OWNERID");
    let version = env::var("LUMENAUTH_VERSION").unwrap_or_else(|_| "1.0".into());
    let secret = env::var("LUMENAUTH_SECRET").expect("set LUMENAUTH_SECRET");
    let email = env::var("LUMENAUTH_EMAIL").unwrap_or_else(|_| "user@app.com".into());
    let password = env::var("LUMENAUTH_PASSWORD").unwrap_or_else(|_| "secret123".into());

    let mut auth =
        LumenAuthClient::new(&name, &ownerid, &version, &secret).with_hwid(hwid());
    if let Ok(url) = env::var("LUMENAUTH_URL") {
        auth = auth.with_url(&url);
    }
    println!("hwid = {}", hwid());

    if let Err(err) = auth.init() {
        eprintln!("init failed: {err}");
        std::process::exit(1);
    }

    let result = match env::var("LUMENAUTH_LICENSE") {
        Ok(license) => auth.register(&email, &password, &license),
        Err(_) => auth.login(&email, &password),
    };
    if let Err(err) = result {
        eprintln!("auth failed: {err}");
        std::process::exit(1);
    }

    match auth.check() {
        Ok(true) => {}
        Ok(false) => {
            eprintln!("session invalid — exiting");
            std::process::exit(1);
        }
        Err(err) => {
            eprintln!("check error: {err}");
            std::process::exit(1);
        }
    }

    let user = auth.user.clone().unwrap();
    println!(
        "welcome {} (level {}, expires {:?})",
        user.email, user.level, user.expires_at
    );

    if let Ok(file_id) = env::var("LUMENAUTH_FILEID") {
        match auth.file(&file_id) {
            Ok(bytes) => println!("downloaded {} bytes", bytes.len()),
            Err(err) => println!("download blocked: {err}"),
        }
    }

    println!(">>> your protected program runs here <<<");
}
