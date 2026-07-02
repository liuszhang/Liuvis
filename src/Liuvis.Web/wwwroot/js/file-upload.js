/**
 * file-upload.js — File upload helpers for Liuvis
 * 
 * Functions called from Blazor via IJSRuntime
 */

/**
 * Get the list of files from a file input element
 * @param {HTMLInputElement} input - File input element reference
 * @returns {string[]} Array of file names
 */
export function getFileList(input) {
    if (!input || !input.files || input.files.length === 0) {
        return [];
    }
    
    const fileNames = [];
    for (let i = 0; i < input.files.length; i++) {
        fileNames.push(input.files[i].name);
    }
    return fileNames;
}

/**
 * Get file stream from a file input element
 * Note: This is a simplified version. In production, use FormData with IJSStreamReference
 * @param {HTMLInputElement} input - File input element reference
 * @returns {Promise<ReadableStream>} File stream
 */
export async function getFileStream(input) {
    if (!input || !input.files || input.files.length === 0) {
        return null;
    }
    
    const file = input.files[0];
    return file.stream();
}

/**
 * Upload file using FormData and fetch API
 * @param {string} url - Upload endpoint URL
 * @param {HTMLInputElement} input - File input element reference
 * @param {string} fileParamName - Form parameter name for the file
 * @returns {Promise<Response>} Fetch response
 */
export async function uploadFile(url, input, fileParamName = 'file') {
    if (!input || !input.files || input.files.length === 0) {
        throw new Error('No file selected');
    }
    
    const formData = new FormData();
    formData.append(fileParamName, input.files[0]);
    
    const response = await fetch(url, {
        method: 'POST',
        body: formData
    });
    
    return response;
}

/**
 * Upload STL file to the import API and return result
 * @param {HTMLInputElement} input - File input element reference
 * @returns {Promise<Object>} Import result with Success, ModelId, FileUrl, and Error
 */
export async function uploadStlFile(input) {
    try {
        if (!input || !input.files || input.files.length === 0) {
            return { success: false, error: 'No file selected' };
        }

        const formData = new FormData();
        formData.append('file', input.files[0]);

        const response = await fetch('/api/import/stl', {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (response.ok && result.success) {
            return {
                success: true,
                modelId: result.data.modelId,
                fileUrl: result.data.fileUrl
            };
        } else {
            return {
                success: false,
                error: result.message || 'Import failed'
            };
        }
    } catch (error) {
        console.error('[file-upload] uploadStlFile error:', error);
        return {
            success: false,
            error: error.message || 'Upload failed'
        };
    }
}

// Make functions available to Blazor
window.getFileList = getFileList;
window.getFileStream = getFileStream;
window.uploadFile = uploadFile;
window.uploadStlFile = uploadStlFile;
