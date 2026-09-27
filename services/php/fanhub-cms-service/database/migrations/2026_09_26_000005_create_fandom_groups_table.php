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
        Schema::create('fandom_groups', function (Blueprint $table) {
            $table->string('id', 64)->primary();
            $table->string('name');
            $table->text('description')->nullable();
            $table->string('status', 50)->default('active');
            $table->string('created_by', 64)->index();
            $table->timestamps();
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('fandom_groups');
    }
};
