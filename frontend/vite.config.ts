import {defineConfig} from 'vite';
export default defineConfig({server:{proxy:{'/api':{target:process.env.API_TARGET || 'http://localhost:5080',changeOrigin:true}}}});
