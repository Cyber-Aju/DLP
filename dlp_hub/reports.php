<?php
// reports.php
require_once 'db_connect.php';

// Get the date from the URL (if set), otherwise use today's date
$date = $_GET['date'] ?? date('Y-m-d');

// 1. Daily User Log (Updated to include ACTIVE_APP and CLIPBOARD_EVENT)
$dailyQuery = $pdo->prepare("
    SELECT machine_name, timestamp, event_type, details 
    FROM telemetry_events 
    WHERE event_type IN ('SESSION_EVENT', 'ACTIVE_APP', 'CLIPBOARD_EVENT') 
    AND DATE(timestamp) = ? 
    ORDER BY timestamp DESC
");
$dailyQuery->execute([$date]);
$dailyLogs = $dailyQuery->fetchAll(PDO::FETCH_ASSOC);

// 2. Breach/Malpractice Report (Updated to also search inside the 'details' column)
$breachQuery = $pdo->prepare("
    SELECT machine_name, timestamp, event_type, details 
    FROM telemetry_events 
    WHERE event_type IN ('MALPRACTICE_BLOCKED', 'MALPRACTICE_WARNING', 'USB_BLOCKED', 'CD_BLOCKED', 'BLUETOOTH_BLOCKED', 'RESTRICTED_FILE_BLOCKED')
    OR details LIKE 'MALPRACTICE_WARNING%'
    ORDER BY timestamp DESC
");
$breachQuery->execute();
$breaches = $breachQuery->fetchAll(PDO::FETCH_ASSOC);

// 3. Hardware Inventory Snapshot Report
$inventoryQuery = $pdo->prepare("
    SELECT machine_name, timestamp, details 
    FROM telemetry_events 
    WHERE event_type = 'DEVICE_INVENTORY' 
    ORDER BY timestamp DESC 
    LIMIT 50
");
$inventoryQuery->execute();
$inventories = $inventoryQuery->fetchAll(PDO::FETCH_ASSOC);
?>

<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>DLP Hub - Telemetry Reports</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 20px 40px;
            background-color: #f4f7f6;
            color: #333;
        }

        h1,
        h2 {
            color: #2c3e50;
        }

        /* Form styling */
        .filter-section {
            background: #fff;
            padding: 15px;
            border-radius: 5px;
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
            margin-bottom: 20px;
            display: inline-block;
        }

        input[type="date"] {
            padding: 5px;
            margin-right: 10px;
        }

        button {
            padding: 6px 15px;
            background-color: #3498db;
            color: white;
            border: none;
            border-radius: 3px;
            cursor: pointer;
        }

        button:hover {
            background-color: #2980b9;
        }

        /* Table styling */
        table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 40px;
            background-color: #fff;
            box-shadow: 0 2px 5px rgba(0, 0, 0, 0.1);
        }

        th,
        td {
            padding: 12px 15px;
            text-align: left;
            border-bottom: 1px solid #ddd;
        }

        th {
            background-color: #2c3e50;
            color: white;
        }

        tr:hover {
            background-color: #f5f5f5;
        }

        /* Empty states & badges */
        .no-data {
            text-align: center;
            color: #888;
            font-style: italic;
            padding: 20px;
        }

        .badge {
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.85em;
            font-weight: bold;
        }

        .badge-warning {
            background-color: #f1c40f;
            color: #333;
        }

        .badge-danger {
            background-color: #e74c3c;
            color: white;
        }
    </style>
</head>

<body>

    <h1>DLP Hub Dashboard</h1>

    <div class="filter-section">
        <form method="GET" action="">
            <label for="date"><strong>Filter Daily Logs by Date:</strong> </label>
            <input type="date" id="date" name="date" value="<?php echo htmlspecialchars($date); ?>">
            <button type="submit">Apply Filter</button>
        </form>
    </div>

    <h2>Session Events (<?php echo htmlspecialchars($date); ?>)</h2>
    <table>
        <thead>
            <tr>
                <th>Machine Name</th>
                <th>Timestamp</th>
                <th>Details</th>
            </tr>
        </thead>
        <tbody>
            <?php if (count($dailyLogs) > 0): ?>
                <?php foreach ($dailyLogs as $log): ?>
                    <tr>
                        <td><?php echo htmlspecialchars($log['machine_name']); ?></td>
                        <td><?php echo htmlspecialchars($log['timestamp']); ?></td>
                        <td><?php echo htmlspecialchars($log['details']); ?></td>
                    </tr>
                <?php endforeach; ?>
            <?php else: ?>
                <tr>
                    <td colspan="3" class="no-data">No session events recorded for this date.</td>
                </tr>
            <?php endif; ?>
        </tbody>
    </table>

    <h2>Security Breaches & Interventions (All Time)</h2>
    <table>
        <thead>
            <tr>
                <th>Machine Name</th>
                <th>Timestamp</th>
                <th>Event Type</th>
                <th>Details</th>
            </tr>
        </thead>
        <tbody>
            <?php if (count($breaches) > 0): ?>
                <?php foreach ($breaches as $breach):
                    // Add a little logic to color-code warnings vs actual blocks
                    $isWarning = strpos($breach['event_type'], 'WARNING') !== false;
                    $badgeClass = $isWarning ? 'badge-warning' : 'badge-danger';
                    ?>
                    <tr>
                        <td><?php echo htmlspecialchars($breach['machine_name']); ?></td>
                        <td><?php echo htmlspecialchars($breach['timestamp']); ?></td>
                        <td><span
                                class="badge <?php echo $badgeClass; ?>"><?php echo htmlspecialchars($breach['event_type']); ?></span>
                        </td>
                        <td><?php echo htmlspecialchars($breach['details']); ?></td>
                    </tr>
                <?php endforeach; ?>
            <?php else: ?>
                <tr>
                    <td colspan="4" class="no-data">Hooray! No security breaches detected.</td>
                </tr>
            <?php endif; ?>
        </tbody>
    </table>

    <h2>Hardware Input Snapshots (Latest 50)</h2>
    <table>
        <thead>
            <tr>
                <th>Machine Name</th>
                <th>Snapshot Time</th>
                <th>Trigger & Connected Devices</th>
            </tr>
        </thead>
        <tbody>
            <?php if (count($inventories) > 0): ?>
                <?php foreach ($inventories as $inv): ?>
                    <tr>
                        <td><?php echo htmlspecialchars($inv['machine_name']); ?></td>
                        <td><?php echo htmlspecialchars($inv['timestamp']); ?></td>
                        <td>
                            <span style="color: #2980b9; font-weight: bold;">
                                <?php echo htmlspecialchars($inv['details']); ?>
                            </span>
                        </td>
                    </tr>
                <?php endforeach; ?>
            <?php else: ?>
                <tr>
                    <td colspan="3" class="no-data">No hardware snapshots recorded yet.</td>
                </tr>
            <?php endif; ?>
        </tbody>
    </table>

</body>

</html>