


import fs from "node:fs";

let seed = 20261107;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const W = 1600, H = 1000;


function vein(startX, startY, angle, length, steps) {
    let x = startX, y = startY, a = angle;
    const points = [[x, y]];
    for (let i = 0; i < steps; i++) {
        a += (rnd() - 0.5) * 0.9;
        const step = length / steps;
        x += Math.cos(a) * step; y += Math.sin(a) * step;
        points.push([x, y]);
    }
    let d = `M${points[0][0].toFixed(1)} ${points[0][1].toFixed(1)}`;
    for (let i = 1; i < points.length - 1; i++) {
        const mx = (points[i][0] + points[i + 1][0]) / 2, my = (points[i][1] + points[i + 1][1]) / 2;
        d += ` Q${points[i][0].toFixed(1)} ${points[i][1].toFixed(1)} ${mx.toFixed(1)} ${my.toFixed(1)}`;
    }
    return { d, points };
}

function veins(count, widthRange, opacityRange, branchEvery) {
    const out = [];
    for (let i = 0; i < count; i++) {
        const fromLeft = rnd() < 0.5;
        const v = vein(fromLeft ? -60 : rnd() * W, fromLeft ? rnd() * H : -60, fromLeft ? -0.35 + rnd() * 0.7 : 0.6 + rnd() * 0.7, 1400 + rnd() * 900, 22);
        const w = widthRange[0] + rnd() * (widthRange[1] - widthRange[0]);
        const o = opacityRange[0] + rnd() * (opacityRange[1] - opacityRange[0]);
        out.push(`<path d="${v.d}" stroke-width="${w.toFixed(2)}" stroke-opacity="${o.toFixed(2)}"/>`);
        for (let k = 3; k < v.points.length - 3; k += branchEvery + Math.floor(rnd() * 3)) {
            const [bx, by] = v.points[k];
            const b = vein(bx, by, rnd() * Math.PI * 2, 160 + rnd() * 320, 8);
            out.push(`<path d="${b.d}" stroke-width="${(w * 0.45).toFixed(2)}" stroke-opacity="${(o * 0.75).toFixed(2)}"/>`);
        }
    }
    return out.join("\n    ");
}

function picture(theme) {
    seed = 20261107;
    const t = theme === "dark"
        ? { base: "#1e1719", cloud: "0 0 0 0 0.40  0 0 0 0 0.19  0 0 0 0 0.26  1.6 0 0 0 -0.62", gold: "#b8913f", pink: "#7a4656", pinkOpacity: 0.55 }
        : { base: "#fbf6f5", cloud: "0 0 0 0 0.94  0 0 0 0 0.74  0 0 0 0 0.79  1.5 0 0 0 -0.62", gold: "#c29a48", pink: "#e2b3c0", pinkOpacity: 0.7 };
    const gold = veins(6, [1.2, 3.2], [0.75, 0.95], 4);
    const pink = veins(9, [0.8, 2.2], [0.45, 0.75], 5);
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid slice">
  <defs>
    <filter id="clouds" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">
      <feTurbulence type="fractalNoise" baseFrequency="0.0022 0.0055" numOctaves="5" seed="11"/>
      <feColorMatrix type="matrix" values="${t.cloud}"/>
    </filter>
    <filter id="wobble" x="-5%" y="-5%" width="110%" height="110%">
      <feTurbulence type="fractalNoise" baseFrequency="0.012" numOctaves="3" seed="5" result="n"/>
      <feDisplacementMap in="SourceGraphic" in2="n" scale="38" xChannelSelector="R" yChannelSelector="G" result="d"/>
      <feGaussianBlur in="d" stdDeviation="0.35"/>
    </filter>
    <filter id="soft" x="-5%" y="-5%" width="110%" height="110%">
      <feTurbulence type="fractalNoise" baseFrequency="0.01" numOctaves="3" seed="9" result="n"/>
      <feDisplacementMap in="SourceGraphic" in2="n" scale="46" xChannelSelector="R" yChannelSelector="G" result="d"/>
      <feGaussianBlur in="d" stdDeviation="0.9"/>
    </filter>
  </defs>
  <rect width="${W}" height="${H}" fill="${t.base}"/>
  <rect width="${W}" height="${H}" filter="url(#clouds)"/>
  <g fill="none" stroke="${t.pink}" stroke-linecap="round" opacity="${t.pinkOpacity}" filter="url(#soft)">
    ${pink}
  </g>
  <g fill="none" stroke="${t.gold}" stroke-linecap="round" filter="url(#wobble)">
    ${gold}
  </g>
</svg>
`;
}

for (const theme of ["light", "dark"]) fs.writeFileSync(new URL(`../_Index/marble-${theme}.svg`, import.meta.url), picture(theme));
console.log("marble-light.svg and marble-dark.svg written");
