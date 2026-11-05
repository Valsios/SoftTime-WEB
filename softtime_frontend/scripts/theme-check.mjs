import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const root = process.cwd();
const stylesPath = join(root, 'src', 'styles.scss');
const styles = readFileSync(stylesPath, 'utf8');

function declarations(block) {
  return Object.fromEntries([...block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map(([, key, value]) => [key, value.trim()]));
}

function block(selector) {
  const match = styles.match(selector);
  if (!match) throw new Error(`Bloc de jetons introuvable : ${selector}`);
  return declarations(match[1]);
}

const light = block(/:root\s*\{([\s\S]*?)\n\}/);
const dark = { ...light, ...block(/:root\[data-theme='dark'\]\s*\{([\s\S]*?)\n\}/) };

function resolve(value, tokens) {
  const resolved = value.replace(/var\((--[\w-]+)\)/g, (_, name) => resolve(tokens[name], tokens));
  if (!/^#[0-9a-f]{6}$/i.test(resolved)) throw new Error(`Couleur non résolue : ${value}`);
  return resolved.toLowerCase();
}

function rgb(hex) {
  return [1, 3, 5].map((i) => Number.parseInt(hex.slice(i, i + 2), 16) / 255);
}

function luminance(hex) {
  return rgb(hex).reduce((total, value, index) => {
    const linear = value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    return total + linear * [0.2126, 0.7152, 0.0722][index];
  }, 0);
}

function luminanceColor(color) {
  return color.reduce((total, value, index) => {
    const linear = value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    return total + linear * [0.2126, 0.7152, 0.0722][index];
  }, 0);
}

function color(value, tokens, backdrop = '#ffffff') {
  let resolved = value;
  let previous;
  do {
    previous = resolved;
    resolved = resolved.replace(/var\((--[\w-]+)\)/g, (_, name) => tokens[name]);
  } while (resolved !== previous);
  if (/^#[0-9a-f]{6}$/i.test(resolved)) return rgb(resolved);
  const match = resolved.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)$/i);
  if (!match) throw new Error(`Couleur non résolue : ${value}`);
  const source = match.slice(1, 4).map((part) => Number(part) / 255);
  const alpha = match[4] === undefined ? 1 : Number(match[4]);
  return source.map((part, index) => part * alpha + rgb(backdrop)[index] * (1 - alpha));
}

function contrast(foreground, background, tokens) {
  const a = luminance(resolve(tokens[foreground], tokens));
  const b = luminance(resolve(tokens[background], tokens));
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

function requireContrast(foreground, background, threshold) {
  const ratio = contrast(foreground, background, dark);
  if (ratio < threshold) throw new Error(`${foreground}/${background}: ${ratio.toFixed(2)}:1 < ${threshold}:1`);
}

const lightReference = {
  '--app-bg': '#f5f7f9', '--app-surface': '#ffffff', '--app-surface-soft': '#f4f7fe',
  '--surface-light': '#f5f7f9', '--surface-dark': '#e9ebf0', '--app-border': '#d6d9e0',
  '--color-border-input': '#878d9a', '--app-text': '#353944', '--app-text-strong': '#0b1332',
  '--text-secondary': '#5b606d', '--text-muted': '#747a88', '--success': '#047857',
  '--warning': '#b45309', '--danger': '#be123c', '--info': '#2e427b',
};

const lightReplacementReference = {
  '--sidebar-brand-text': '#ffffff', '--sidebar-brand-sub': 'rgba(255, 255, 255, 0.6)',
  '--sidebar-border': 'rgba(0, 0, 0, 0.15)', '--sidebar-brand-border': 'rgba(255, 255, 255, 0.08)',
  '--sidebar-hover': 'rgba(255, 255, 255, 0.05)', '--sidebar-active-bg': 'rgba(255, 255, 255, 0.08)',
  '--sidebar-text': 'rgba(255, 255, 255, 0.80)', '--sidebar-text-muted': 'rgba(255, 255, 255, 0.45)',
  '--sidebar-text-hover': '#ffffff', '--text-on-gold': 'var(--color-navy-950)',
  '--scrollbar-thumb': 'var(--neutral-300)', '--scrollbar-thumb-hover': 'var(--neutral-500)',
  '--toggle-track': 'var(--neutral-300)', '--border-hover': 'var(--neutral-300)',
  '--card-hover-bg': 'var(--app-surface)', '--thead-bg': 'var(--surface-light)',
  '--focus-glow': 'rgba(46, 66, 123, 0.1)', '--bar-default': 'var(--primary-500)',
};

for (const [name, expected] of Object.entries(lightReference)) {
  const actual = resolve(light[name], light);
  if (actual !== expected) throw new Error(`Régression thème clair : ${name} vaut ${actual}, attendu ${expected}`);
}
for (const [name, expected] of Object.entries(lightReplacementReference)) {
  if (light[name] !== expected) throw new Error(`Régression thème clair : ${name} vaut ${light[name]}, attendu ${expected}`);
}

for (const surface of ['--surface-0', '--surface-1', '--surface-2', '--surface-3']) {
  requireContrast('--text-primary', surface, 7);
  requireContrast('--text-secondary', surface, 4.5);
}
requireContrast('--accent', '--surface-0', 4.5);
requireContrast('--text-on-accent', '--accent', 4.5);
requireContrast('--border-subtle', '--surface-1', 1.8);
requireContrast('--border-control', '--surface-1', 3);
requireContrast('--focus-ring', '--surface-1', 3);
requireContrast('--text-primary', '--surface-2', 4.5);
requireContrast('--text-primary', '--info-bg', 4.5);
for (const tokens of [light, dark]) {
  const themeName = tokens === light ? 'clair' : 'sombre';
  for (const [foreground, background] of [
    ['--sidebar-text', '--sidebar-bg'],
    ['--sidebar-text', '--sidebar-hover'],
    ['--sidebar-active-color', '--sidebar-active-bg'],
  ]) {
    const sidebar = resolve(tokens['--sidebar-bg'], tokens);
    const backgroundColor = color(tokens[background], tokens, sidebar);
    const foregroundColor = color(tokens[foreground], tokens, `#${backgroundColor.map((value) => Math.round(value * 255).toString(16).padStart(2, '0')).join('')}`);
    const ratio = (Math.max(luminanceColor(foregroundColor), luminanceColor(backgroundColor)) + 0.05) /
      (Math.min(luminanceColor(foregroundColor), luminanceColor(backgroundColor)) + 0.05);
    if (ratio < 4.5) throw new Error(`Menu ${themeName} ${foreground}/${background}: ${ratio.toFixed(2)}:1 < 4.5:1`);
  }
}
for (const status of ['--success', '--warning', '--danger', '--info']) requireContrast(status, `${status}-bg`, 4.5);

const exceptions = new Set([
  'src/app/features/params/code-constantes.ts',
  'src/app/features/reports/audit.ts',
]);
const colorLiteral = /#[0-9a-f]{3,8}\b|rgba?\(/i;
function files(directory) {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    return statSync(path).isDirectory() ? files(path) : [path];
  });
}
for (const file of files(join(root, 'src', 'app'))) {
  const path = relative(root, file).replaceAll('\\', '/');
  if (!/\.(ts|scss)$/.test(path) || exceptions.has(path) || /privileges/.test(path)) continue;
  if (colorLiteral.test(readFileSync(file, 'utf8'))) throw new Error(`Couleur directe interdite hors jetons : ${path}`);
}

console.log('Vérification des jetons clair/sombre et contrastes : OK');
