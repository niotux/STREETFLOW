function xmlAttribute(value) {
  return String(value).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll("'", '&apos;');
}

export function vehicleSettingsXml(settings) {
  const attributes = [
    ['id', settings.id], ['vClass', settings.vehicleClass],
    ['minGap', settings.minGap], ['tau', settings.headway],
    ['speedFactor', settings.speedFactor], ['speedDev', settings.speedDeviation],
    ['accel', settings.acceleration], ['decel', settings.deceleration]
  ];
  const values = [];
  for (const [key, value] of attributes) values.push(`${key}="${xmlAttribute(value)}"`);
  return `<?xml version="1.0" encoding="UTF-8"?>\n<additional>\n  <vType ${values.join(' ')} />\n</additional>\n`;
}
