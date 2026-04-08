<?php
header('Content-Type: application/json');
require_once 'db_connect.php';

// 1. Read the raw incoming text from the C# agent
$raw_input = file_get_contents('php://input');

// ==========================================
// DEBUG: LOG RAW PAYLOAD TO A TEXT FILE
// ==========================================
// This will create a file called 'payload_log.txt' in your dlp_hub folder.
$log_message = "[" . date('Y-m-d H:i:s') . "] INCOMING NETWORK TRAFFIC:\n" . $raw_input . "\n\n";
file_put_contents('payload_log.txt', $log_message, FILE_APPEND);
// ==========================================

// 2. Decode the JSON so PHP can use it
$data = json_decode($raw_input, true);

if (!$data) {
    http_response_code(400); // Tell C# to STOP and keep the logs!
    echo json_encode(['status' => 'error', 'message' => 'Invalid JSON payload']);
    exit;
}

$tenant_key = $data['tenant_key'] ?? '';
$machine = $data['machine_name'] ?? 'UNKNOWN';
$secure_payload = $data['secure_payload'] ?? ''; // Grab the scrambled string

// 1. Authenticate Tenant
$stmt = $pdo->prepare("SELECT id FROM companies WHERE tenant_key = ?");
$stmt->execute([$tenant_key]);
$company = $stmt->fetch(PDO::FETCH_ASSOC);

if (!$company) {
    echo json_encode(['status' => 'error', 'message' => 'Invalid Tenant Key']);
    exit;
}
$company_id = $company['id'];

// ==========================================
// DECRYPT THE ENTERPRISE PAYLOAD
// ==========================================
$events = [];
if (!empty($secure_payload)) {
    // 1. Hash the Tenant Key to get the exact same 32-byte AES key the C# agent used
    $aes_key = hash('sha256', $tenant_key, true);

    // 2. Decode the Base64 transmission
    $decoded_data = base64_decode($secure_payload);

    // 3. Separate the 16-byte IV from the rest of the CipherText
    $iv = substr($decoded_data, 0, 16);
    $ciphertext = substr($decoded_data, 16);

    // 4. Decrypt!
    $decrypted_json = openssl_decrypt($ciphertext, 'aes-256-cbc', $aes_key, OPENSSL_RAW_DATA, $iv);

    if ($decrypted_json !== false) {
        // Convert the unlocked JSON string back into a PHP array
        $events = json_decode($decrypted_json, true);
    } else {
        echo json_encode(['status' => 'error', 'message' => 'Decryption failed. Invalid Key.']);
        exit;
    }
}

// 2. Insert Telemetry Events
if (!empty($events)) {
    $insertStmt = $pdo->prepare("INSERT INTO telemetry_events (company_id, machine_name, timestamp, event_type, details) VALUES (?, ?, ?, ?, ?)");
    foreach ($events as $event) {
        $original_time = $event['timestamp'] ?? date('Y-m-d H:i:s');
        $insertStmt->execute([$company_id, $machine, $original_time, $event['type'], $event['details']]);
    }
}

// 3. Fetch Policy for this Machine
$polStmt = $pdo->prepare("SELECT * FROM device_policies WHERE machine_name = ? AND company_id = ?");
$polStmt->execute([$machine, $company_id]);
$policy = $polStmt->fetch(PDO::FETCH_ASSOC);

if (!$policy) {
    // Default policy if none exists
    $policy = [
        'block_usb' => 0,
        'block_cd' => 0,
        'block_bt' => 0,
        'upload_blocked' => 0,
        'tracked_folders' => '[]',
        'banned_keywords' => '[]',
        'enforcement_mode' => 'WARN'
    ];
}

// 4. Return Policy to Agent
echo json_encode([
    'status' => 'success',
    'policy' => [
        'usb_blocked' => (bool) $policy['block_usb'],
        'cd_blocked' => (bool) $policy['block_cd'],
        'bt_blocked' => (bool) $policy['block_bt'],
        'upload_blocked' => (bool) $policy['upload_blocked'],

        'folders' => json_decode($policy['tracked_folders']),
        'keywords' => json_decode($policy['banned_keywords']),
        'mode' => $policy['enforcement_mode']
    ]
]);
?>