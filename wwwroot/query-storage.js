export function load(key) { return window.localStorage.getItem(key); }
export function save(key, json) { window.localStorage.setItem(key, json); }
export function remove(key) { window.localStorage.removeItem(key); }