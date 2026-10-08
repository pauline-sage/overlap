/** Strip image metadata and normalize a chosen photo to the existing canonical RGB format. */
export async function drawingPixels(file: File): Promise<string> {
  if (!['image/jpeg','image/png','image/webp'].includes(file.type) || file.size > 10*1024*1024)
    throw new Error('Choose a JPEG, PNG or WebP photo smaller than 10 MB.');
  const bitmap = await createImageBitmap(file);
  try {
    if (bitmap.width*bitmap.height > 40000000) throw new Error('Choose a smaller image.');
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 512;
    const context = canvas.getContext('2d'); if (!context) throw new Error('Photo processing is unavailable.');
    context.fillStyle = '#ffffff'; context.fillRect(0,0,512,512);
    const scale = Math.min(512/bitmap.width,512/bitmap.height);
    const width = bitmap.width*scale, height = bitmap.height*scale;
    context.drawImage(bitmap,(512-width)/2,(512-height)/2,width,height);
    const rgba = context.getImageData(0,0,512,512).data, rgb = new Uint8Array(512*512*3);
    for (let i=0,j=0;i<rgba.length;i+=4) {rgb[j++]=rgba[i];rgb[j++]=rgba[i+1];rgb[j++]=rgba[i+2];}
    let binary=''; for(let i=0;i<rgb.length;i+=8192) binary+=String.fromCharCode(...rgb.subarray(i,i+8192));
    return btoa(binary);
  } finally { bitmap.close(); }
}

export interface DrawingPhoto {accountId:string;revision:string;uploadedUtc:string;mine:boolean;myRating:number|null;partnerRating:number|null;}
export interface DrawingCategory {id:string;title:string;count:number;}
export interface DrawingRound {currentConnection?:boolean;categories?:string[];sessionId?:string|null;drawingCount?:number;drawingNumber?:number;nextCreated?:boolean;id:number;prompt:string;seconds:number;revision:string;mine:boolean;status:'waiting'|'drawing'|'paused'|'finished'|'cancelled';pausedSeconds?:number|null;createdUtc:string;startUtc:string|null;endUtc:string|null;photos:DrawingPhoto[];}
export interface DrawingList {categories?:DrawingCategory[];connected:boolean;serverUtc:string;rounds:DrawingRound[];}
