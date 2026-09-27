<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class UserProjection extends Model
{
    use HasFactory;

    protected $table = 'users_projection';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'full_name',
        'avatar_url',
        'email',
        'status',
    ];

    protected $appends = [
        'title',
    ];

    protected static function booted(): void
    {
        static::creating(function (UserProjection $user) {
            if (empty($user->id)) {
                $user->id = 'usr_' . Str::lower(Str::random(12));
            }
            if (empty($user->status)) {
                $user->status = 'active';
            }
        });
    }

    public function getTitleAttribute(): ?string
    {
        return $this->attributes['full_name'] ?? null;
    }

    public function setTitleAttribute(?string $value): void
    {
        $this->attributes['full_name'] = $value;
    }
}
