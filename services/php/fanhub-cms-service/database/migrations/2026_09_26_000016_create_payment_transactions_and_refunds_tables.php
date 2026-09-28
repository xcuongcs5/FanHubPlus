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
        if (!Schema::hasTable('payment_transactions')) {
            Schema::create('payment_transactions', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('user_id', 64)->nullable()->index();
                $table->string('user_name', 255)->nullable();
                $table->decimal('amount', 19, 4)->default(0);
                $table->string('provider', 50)->default('VNPay');
                $table->string('merchant_ref', 100)->nullable();
                $table->string('status', 50)->default('Success');
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('payment_refunds')) {
            Schema::create('payment_refunds', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('booking_id', 64)->nullable()->index();
                $table->string('user_id', 64)->nullable()->index();
                $table->string('user_name', 255)->nullable();
                $table->decimal('amount', 19, 4)->default(0);
                $table->string('reason', 1000)->nullable();
                $table->string('note', 1000)->nullable();
                $table->string('status', 50)->default('Pending');
                $table->timestamps();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('payment_refunds');
        Schema::dropIfExists('payment_transactions');
    }
};
