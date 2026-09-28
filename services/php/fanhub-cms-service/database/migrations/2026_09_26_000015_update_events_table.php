<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        Schema::table('events', function (Blueprint $table) {
            if (!Schema::hasColumn('events', 'organizer')) {
                $table->string('organizer', 255)->nullable()->after('status');
            }
            if (!Schema::hasColumn('events', 'organizer_email')) {
                $table->string('organizer_email', 255)->nullable()->after('organizer');
            }
            if (!Schema::hasColumn('events', 'location')) {
                $table->string('location', 255)->nullable()->after('organizer_email');
            }
            if (!Schema::hasColumn('events', 'start_time')) {
                $table->string('start_time', 50)->nullable()->after('location');
            }
            if (!Schema::hasColumn('events', 'end_time')) {
                $table->string('end_time', 50)->nullable()->after('start_time');
            }
            if (!Schema::hasColumn('events', 'admin_note')) {
                $table->text('admin_note')->nullable()->after('end_time');
            }
            if (!Schema::hasColumn('events', 'ai_risk_score')) {
                $table->decimal('ai_risk_score', 5, 2)->default(0.05)->after('admin_note');
            }
            if (!Schema::hasColumn('events', 'ticket_types_json')) {
                $table->text('ticket_types_json')->nullable()->after('ai_risk_score');
            }
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('events', function (Blueprint $table) {
            $cols = [
                'organizer',
                'organizer_email',
                'location',
                'start_time',
                'end_time',
                'admin_note',
                'ai_risk_score',
                'ticket_types_json',
            ];
            foreach ($cols as $col) {
                if (Schema::hasColumn('events', $col)) {
                    $table->dropColumn($col);
                }
            }
        });
    }
};
