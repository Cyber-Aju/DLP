<?php
require_once 'db_connect.php';

// This MUST match the LICENSE_SECRET in your C# LicenseManager.cs!
$SECRET_KEY = "AEROLOGUE_MASTER_SIGNING_SECRET_999!";

$message = "";
$generated_key = "";

if ($_SERVER["REQUEST_METHOD"] == "POST") {
    $org_name = trim($_POST["org_name"]);
    $expiry_date = $_POST["expiry_date"]; // Format: YYYY-MM-DD

    if (!empty($org_name) && !empty($expiry_date)) {
        // 1. Create the JSON Payload
        $payload_array = [
            "org" => $org_name,
            "expiry" => $expiry_date . "T23:59:59Z" // Set to expire at midnight
        ];
        $json_payload = json_encode($payload_array);

        // 2. Base64 Encode the Payload
        $base64_payload = base64_encode($json_payload);

        // 3. Generate the Cryptographic Signature (HMAC-SHA256)
        $raw_signature = hash_hmac('sha256', $base64_payload, $SECRET_KEY, true);
        $base64_signature = base64_encode($raw_signature);

        // 4. Combine them to create the final License Key
        $generated_key = $base64_payload . "." . $base64_signature;

        // 5. Save to Database
        try {
            $pdo->beginTransaction();

            // Insert Company
            $stmt = $pdo->prepare("INSERT INTO companies (name, tenant_key) VALUES (?, ?)");
            $stmt->execute([$org_name, $generated_key]);
            $new_company_id = $pdo->lastInsertId();

            // Generate a Default Policy for this company so the agent works immediately!
            $polStmt = $pdo->prepare("INSERT INTO device_policies (company_id, machine_name, block_usb, block_cd, block_bt, tracked_folders, banned_keywords, enforcement_mode, upload_blocked) VALUES (?, 'DEFAULT', 1, 1, 1, '[]', '[]', 'WARN', 0)");
            $polStmt->execute([$new_company_id]);

            $pdo->commit();
            $message = "<div class='success'>Organization Created & License Generated Successfully!</div>";
        } catch (Exception $e) {
            $pdo->rollBack();
            $message = "<div class='error'>Database Error: " . $e->getMessage() . "</div>";
        }
    } else {
        $message = "<div class='error'>Please fill in all fields.</div>";
    }
}
?>

<!DOCTYPE html>
<html>

<head>
    <title>Aerologue DLP - License Generator</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #f4f7f6;
            padding: 40px;
        }

        .container {
            max-width: 600px;
            background: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 4px 15px rgba(0, 0, 0, 0.1);
            margin: auto;
        }

        h2 {
            color: #2c3e50;
            border-bottom: 2px solid #3498db;
            padding-bottom: 10px;
        }

        label {
            font-weight: bold;
            display: block;
            margin-top: 15px;
            color: #34495e;
        }

        input[type="text"],
        input[type="date"] {
            width: 100%;
            padding: 10px;
            margin-top: 5px;
            border: 1px solid #ccc;
            border-radius: 4px;
            box-sizing: border-box;
        }

        button {
            margin-top: 20px;
            width: 100%;
            padding: 12px;
            background: #2980b9;
            color: white;
            border: none;
            border-radius: 4px;
            font-size: 16px;
            cursor: pointer;
        }

        button:hover {
            background: #3498db;
        }

        .success {
            background: #d4edda;
            color: #155724;
            padding: 10px;
            border-radius: 4px;
            margin-bottom: 15px;
        }

        .error {
            background: #f8d7da;
            color: #721c24;
            padding: 10px;
            border-radius: 4px;
            margin-bottom: 15px;
        }

        .key-box {
            margin-top: 20px;
            padding: 15px;
            background: #2c3e50;
            color: #ecf0f1;
            border-radius: 4px;
            word-wrap: break-word;
            font-family: monospace;
        }
    </style>
</head>

<body>
    <div class="container">
        <h2>Generate Enterprise License</h2>
        <?= $message ?>

        <form method="POST">
            <label>Organization Name:</label>
            <input type="text" name="org_name" placeholder="e.g., Stark Industries" required>

            <label>License Expiration Date:</label>
            <input type="date" name="expiry_date" required>

            <button type="submit">Generate Cryptographic Key</button>
        </form>

        <?php if ($generated_key): ?>
            <div class="key-box">
                <strong>Copy this License Key:</strong><br><br>
                <?= htmlspecialchars($generated_key) ?>
            </div>
        <?php endif; ?>
    </div>
</body>

</html>